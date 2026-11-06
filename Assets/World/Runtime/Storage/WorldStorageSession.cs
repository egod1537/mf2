using System;
using System.Collections.Generic;
using MF2.World.Core;
using Unity.Mathematics;

namespace MF2.World.Storage
{
    public readonly struct WorldSaveResult
    {
        public readonly int RegionsWritten;
        public readonly int SnapshotsWritten;
        public readonly int DirtySnapshotsAccepted;
        public readonly int DirtySnapshotsRetained;

        internal WorldSaveResult(
            int regionsWritten,
            int snapshotsWritten,
            int dirtySnapshotsAccepted,
            int dirtySnapshotsRetained)
        {
            RegionsWritten = regionsWritten;
            SnapshotsWritten = snapshotsWritten;
            DirtySnapshotsAccepted = dirtySnapshotsAccepted;
            DirtySnapshotsRetained = dirtySnapshotsRetained;
        }
    }

    /// <summary>
    /// Tracks data-dirty chunk versions independently from WorldCore's one-flush dirty view.
    /// A snapshot is acknowledged only after its region save succeeds and its handle/version is
    /// still current. Failed or stale saves remain dirty.
    /// </summary>
    public sealed class WorldStorageSession
    {
        private readonly Dictionary<int3, DirtyToken> dirtyChunks =
            new Dictionary<int3, DirtyToken>();

        public int DirtyChunkCount => dirtyChunks.Count;

        public bool IsDirty(int3 chunkCoordinate)
        {
            return dirtyChunks.ContainsKey(chunkCoordinate);
        }

        public bool CanEvict(int3 chunkCoordinate)
        {
            return !dirtyChunks.ContainsKey(chunkCoordinate);
        }

        /// <summary>
        /// Residency code must use this gate instead of unloading a dirty chunk directly.
        /// Pre-eviction saving can be layered on this seam when residency is implemented.
        /// </summary>
        public bool TryUnloadCleanChunk(WorldCore world, int3 chunkCoordinate)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            return CanEvict(chunkCoordinate) && world.TryUnloadChunk(chunkCoordinate);
        }

        public void TrackDirty(WorldCore world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            var currentDirty = world.DirtyChunks;
            for (var i = 0; i < currentDirty.Length; i++)
            {
                var dirty = currentDirty[i];
                if ((dirty.Flags & ChunkDirtyFlags.Data) == 0)
                    continue;

                dirtyChunks[dirty.ChunkCoordinate] =
                    new DirtyToken(dirty.Handle, dirty.Version);
            }
        }

        public void Clear()
        {
            dirtyChunks.Clear();
        }

        public WorldSaveResult SaveDirty(WorldCore world, IWorldStorage storage)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));

            world.CompleteDataDependencies();
            var coordinates = new List<int3>(dirtyChunks.Keys);
            coordinates.Sort(CompareCoordinates);

            var capturesByRegion = new Dictionary<int3, List<CapturedChunk>>();
            for (var i = 0; i < coordinates.Count; i++)
            {
                var coordinate = coordinates[i];
                var token = RefreshToken(world, coordinate, dirtyChunks[coordinate]);
                dirtyChunks[coordinate] = token;
                var captured = Capture(world, coordinate, token);
                var region = RegionCoordinates.ToRegion(coordinate);
                if (!capturesByRegion.TryGetValue(region, out var captures))
                {
                    captures = new List<CapturedChunk>();
                    capturesByRegion.Add(region, captures);
                }

                captures.Add(captured);
            }

            var regions = new List<int3>(capturesByRegion.Keys);
            regions.Sort(CompareCoordinates);
            var accepted = 0;
            var snapshotsWritten = 0;
            for (var regionIndex = 0; regionIndex < regions.Count; regionIndex++)
            {
                var region = regions[regionIndex];
                var captures = capturesByRegion[region];
                var data = new StoredChunk[captures.Count];
                for (var i = 0; i < captures.Count; i++)
                    data[i] = captures[i].Data;

                storage.SaveRegion(region, data);
                snapshotsWritten += captures.Count;

                for (var i = 0; i < captures.Count; i++)
                {
                    var capture = captures[i];
                    if (!dirtyChunks.TryGetValue(capture.Data.ChunkCoordinate, out var tracked) ||
                        !tracked.Equals(capture.Token))
                    {
                        continue;
                    }

                    if (world.IsChunkVersionCurrent(capture.Token.Handle, capture.Token.Version))
                    {
                        dirtyChunks.Remove(capture.Data.ChunkCoordinate);
                        accepted++;
                    }
                    else if (world.TryGetChunkHandle(capture.Data.ChunkCoordinate, out var currentHandle) &&
                             currentHandle == capture.Token.Handle &&
                             world.TryGetChunkVersion(currentHandle, out var currentVersion))
                    {
                        dirtyChunks[capture.Data.ChunkCoordinate] =
                            new DirtyToken(currentHandle, currentVersion);
                    }
                }
            }

            return new WorldSaveResult(
                regions.Count,
                snapshotsWritten,
                accepted,
                dirtyChunks.Count);
        }

        private static DirtyToken RefreshToken(
            WorldCore world,
            int3 chunkCoordinate,
            DirtyToken tracked)
        {
            if (!world.TryGetChunkHandle(chunkCoordinate, out var handle))
            {
                throw new InvalidOperationException(
                    $"Dirty chunk {chunkCoordinate} was unloaded before a successful save.");
            }
            if (handle != tracked.Handle)
            {
                throw new InvalidOperationException(
                    $"Dirty chunk {chunkCoordinate} was recycled before a successful save.");
            }
            if (!world.TryGetChunkVersion(handle, out var version))
                throw new InvalidOperationException($"Cannot read version for dirty chunk {chunkCoordinate}.");

            return version == tracked.Version ? tracked : new DirtyToken(handle, version);
        }

        private static CapturedChunk Capture(
            WorldCore world,
            int3 chunkCoordinate,
            DirtyToken token)
        {
            if (!world.TryAcquireChunkLease(token.Handle, out var lease))
            {
                throw new InvalidOperationException(
                    $"Cannot lease dirty chunk {chunkCoordinate} for a save snapshot.");
            }

            var blocks = new List<StoredBlock>();
            try
            {
                for (var index = 0; index < ChunkCoordinates.Volume; index++)
                {
                    var block = lease.GetBlock(index);
                    if (!block.IsAir)
                        blocks.Add(new StoredBlock(index, block.Id, block.State));
                }
            }
            finally
            {
                if (!world.ReleaseChunkLease(lease))
                    throw new InvalidOperationException("Failed to release a storage snapshot lease.");
            }

            if (!world.IsChunkVersionCurrent(token.Handle, token.Version))
            {
                throw new InvalidOperationException(
                    $"Chunk {chunkCoordinate} changed while its save snapshot was captured.");
            }

            return new CapturedChunk(new StoredChunk(chunkCoordinate, blocks), token);
        }

        private static int CompareCoordinates(int3 left, int3 right)
        {
            var x = left.x.CompareTo(right.x);
            if (x != 0)
                return x;
            var y = left.y.CompareTo(right.y);
            return y != 0 ? y : left.z.CompareTo(right.z);
        }

        private readonly struct DirtyToken : IEquatable<DirtyToken>
        {
            public readonly ChunkHandle Handle;
            public readonly ulong Version;

            public DirtyToken(ChunkHandle handle, ulong version)
            {
                Handle = handle;
                Version = version;
            }

            public bool Equals(DirtyToken other)
            {
                return Handle == other.Handle && Version == other.Version;
            }
        }

        private readonly struct CapturedChunk
        {
            public readonly StoredChunk Data;
            public readonly DirtyToken Token;

            public CapturedChunk(StoredChunk data, DirtyToken token)
            {
                Data = data;
                Token = token;
            }
        }
    }
}
