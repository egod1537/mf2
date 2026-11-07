using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using MF2.World.Core;
using MF2.World.Storage;

namespace MF2.Game.Domain
{
    public interface IReadOnlySimulationWorld
    {
        Block GetBlockOrAir(int3 position);
        int LoadedChunkCount { get; }
    }

    public readonly struct WorldBlockDelta
    {
        public WorldBlockDelta(int3 position, Block original, Block final)
        {
            Position = position;
            Original = original;
            Final = final;
        }

        public int3 Position { get; }
        public Block Original { get; }
        public Block Final { get; }
    }

    public readonly struct WorldDirtyChunkDelta
    {
        public WorldDirtyChunkDelta(int3 coordinate, ulong version, ChunkDirtyFlags flags)
        {
            Coordinate = coordinate;
            Version = version;
            Flags = flags;
        }

        public int3 Coordinate { get; }
        public ulong Version { get; }
        public ChunkDirtyFlags Flags { get; }
    }

    public sealed class TickDelta
    {
        private readonly IReadOnlyList<WorldBlockDelta> blockChanges;
        private readonly IReadOnlyList<WorldDirtyChunkDelta> dirtyChunks;

        internal TickDelta(WorldBlockDelta[] blockChanges, WorldDirtyChunkDelta[] dirtyChunks)
        {
            if (blockChanges == null)
                throw new ArgumentNullException(nameof(blockChanges));
            if (dirtyChunks == null)
                throw new ArgumentNullException(nameof(dirtyChunks));
            this.blockChanges = Array.AsReadOnly(blockChanges);
            this.dirtyChunks = Array.AsReadOnly(dirtyChunks);
        }

        public IReadOnlyList<WorldBlockDelta> BlockChanges => blockChanges;
        public IReadOnlyList<WorldDirtyChunkDelta> DirtyChunks => dirtyChunks;
    }

    /// <summary>Single owner of the server session's WorldCore and dirty-save tracker.</summary>
    public sealed class SimulationWorld : IReadOnlySimulationWorld, IDisposable
    {
        private readonly WorldStorageSession storageSession = new WorldStorageSession();
        private WorldCore core = new WorldCore();
        private bool disposed;

        public WorldCore Core
        {
            get
            {
                ThrowIfDisposed();
                return core;
            }
        }

        public int LoadedChunkCount
        {
            get
            {
                ThrowIfDisposed();
                return core.LoadedChunkCount;
            }
        }

        public int DirtyStorageChunkCount
        {
            get
            {
                ThrowIfDisposed();
                return storageSession.DirtyChunkCount;
            }
        }

        public void Enqueue(WorldCommand command)
        {
            ThrowIfDisposed();
            core.Enqueue(command);
        }

        public bool CanEnqueue(WorldCommand command)
        {
            ThrowIfDisposed();
            try
            {
                core.ValidateCommand(command);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public TickDelta FlushAndCapture()
        {
            ThrowIfDisposed();
            core.Flush();
            storageSession.TrackDirty(core);

            var nativeChanges = core.Changes;
            var changes = new WorldBlockDelta[nativeChanges.Length];
            for (var i = 0; i < nativeChanges.Length; i++)
            {
                var change = nativeChanges[i];
                changes[i] = new WorldBlockDelta(change.WorldPosition, change.Original, change.Final);
            }

            var nativeDirty = core.DirtyChunks;
            var dirty = new WorldDirtyChunkDelta[nativeDirty.Length];
            for (var i = 0; i < nativeDirty.Length; i++)
            {
                var chunk = nativeDirty[i];
                dirty[i] = new WorldDirtyChunkDelta(
                    chunk.ChunkCoordinate,
                    chunk.Version,
                    chunk.Flags);
            }

            return new TickDelta(changes, dirty);
        }

        public Block GetBlockOrAir(int3 position)
        {
            ThrowIfDisposed();
            return core.GetBlockOrAir(position);
        }

        public WorldSaveResult SaveDirty(IWorldStorage storage)
        {
            ThrowIfDisposed();
            return storageSession.SaveDirty(core, storage);
        }

        public WorldStorageSnapshot Load(IWorldStorage storage)
        {
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));

            ThrowIfDisposed();
            var snapshot = storage.LoadAll();
            Restore(snapshot);
            return snapshot;
        }

        public void Restore(WorldStorageSnapshot snapshot)
        {
            ThrowIfDisposed();
            var replacement = WorldStorageWorldBuilder.CreateWorld(snapshot);
            var previous = core;
            core = replacement;
            storageSession.Clear();
            previous.Dispose();
        }

        public WorldStorageSnapshot CaptureSnapshot()
        {
            ThrowIfDisposed();
            core.CompleteDataDependencies();
            using (var handles = core.GetLoadedChunkHandles(Allocator.Temp))
            {
                var chunks = new List<StoredChunk>(handles.Length);
                for (var i = 0; i < handles.Length; i++)
                {
                    var handle = handles[i];
                    if (!core.TryGetChunkVersion(handle, out var version) ||
                        !core.TryAcquireChunkLease(handle, out var lease))
                    {
                        throw new InvalidOperationException("A world chunk changed before snapshot capture.");
                    }

                    var blocks = new List<StoredBlock>();
                    try
                    {
                        for (var blockIndex = 0; blockIndex < ChunkCoordinates.Volume; blockIndex++)
                        {
                            var block = lease.GetBlock(blockIndex);
                            if (!block.IsAir)
                                blocks.Add(new StoredBlock(blockIndex, block.Id, block.State));
                        }
                    }
                    finally
                    {
                        if (!core.ReleaseChunkLease(lease))
                            throw new InvalidOperationException("Failed to release a world snapshot lease.");
                    }

                    if (!core.IsChunkVersionCurrent(handle, version))
                        throw new InvalidOperationException("A world chunk changed during snapshot capture.");
                    chunks.Add(new StoredChunk(lease.ChunkCoordinate, blocks));
                }
                return new WorldStorageSnapshot(chunks);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            core.Dispose();
            core = null;
            storageSession.Clear();
            disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(SimulationWorld));
        }
    }
}
