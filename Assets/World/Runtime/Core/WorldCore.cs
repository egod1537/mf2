using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MF2.World.Core
{
    /// <summary>
    /// Owns the sparse native chunk store and is the only block mutation boundary.
    /// This type is intentionally independent of MonoBehaviour and GameObject lifetimes.
    /// </summary>
    public sealed class WorldCore : IDisposable
    {
        public const int DefaultMaxFillBlocksPerCommand = 262_144;

        private ChunkPool pool;
        private NativeParallelHashMap<int3, ChunkHandle> chunks;
        private NativeList<WorldCommand> commands;
        private NativeList<BlockChange> changes;
        private NativeParallelHashMap<int3, int> changeIndices;
        private NativeList<DirtyChunk> dirtyChunks;
        private NativeParallelHashMap<int3, int> dirtyIndices;
        private JobHandle dataDependency;
        private bool hasDataDependency;
        private bool disposed;
        private readonly int maxFillBlocksPerCommand;

        public WorldCore(
            int initialChunkCapacity = 4,
            int initialCommandCapacity = 64,
            int maxFillBlocksPerCommand = DefaultMaxFillBlocksPerCommand,
            Allocator allocator = Allocator.Persistent)
        {
            if (initialChunkCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(initialChunkCapacity));
            if (initialCommandCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(initialCommandCapacity));
            if (maxFillBlocksPerCommand < 1)
                throw new ArgumentOutOfRangeException(nameof(maxFillBlocksPerCommand));

            this.maxFillBlocksPerCommand = maxFillBlocksPerCommand;
            pool = new ChunkPool(initialChunkCapacity, allocator);
            chunks = new NativeParallelHashMap<int3, ChunkHandle>(initialChunkCapacity, allocator);
            commands = new NativeList<WorldCommand>(initialCommandCapacity, allocator);
            changes = new NativeList<BlockChange>(initialCommandCapacity, allocator);
            changeIndices = new NativeParallelHashMap<int3, int>(initialCommandCapacity, allocator);
            dirtyChunks = new NativeList<DirtyChunk>(initialChunkCapacity, allocator);
            dirtyIndices = new NativeParallelHashMap<int3, int>(initialChunkCapacity, allocator);
        }

        public int LoadedChunkCount
        {
            get
            {
                ThrowIfDisposed();
                return chunks.Count();
            }
        }

        public int ChunkPoolCapacity
        {
            get
            {
                ThrowIfDisposed();
                return pool.Capacity;
            }
        }

        public int PendingCommandCount
        {
            get
            {
                ThrowIfDisposed();
                return commands.Length;
            }
        }

        /// <summary>
        /// One original/final record per cell touched by the most recent Flush.
        /// The view remains valid only until the next Flush or Dispose.
        /// </summary>
        public NativeArray<BlockChange> Changes
        {
            get
            {
                ThrowIfDisposed();
                return changes.AsArray();
            }
        }

        /// <summary>
        /// Coalesced dirty chunks from the most recent Flush.
        /// The view remains valid only until the next Flush or Dispose.
        /// </summary>
        public NativeArray<DirtyChunk> DirtyChunks
        {
            get
            {
                ThrowIfDisposed();
                return dirtyChunks.AsArray();
            }
        }

        public void Enqueue(WorldCommand command)
        {
            ThrowIfDisposed();
            if (command.Type == WorldCommandType.None)
                throw new ArgumentException("An empty world command cannot be enqueued.", nameof(command));
            if (command.Block.Id == 0 && command.Block.State != 0)
                throw new ArgumentException("Air commands must use state zero.", nameof(command));

            var expandedCount = command.ExpandedCellCount;
            if (command.Type == WorldCommandType.FillRegion &&
                expandedCount > maxFillBlocksPerCommand)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(command),
                    $"FillRegion expands to {expandedCount} blocks; the configured limit is " +
                    $"{maxFillBlocksPerCommand}.");
            }

            commands.Add(command);
        }

        /// <summary>
        /// Applies queued commands in insertion order. Registered jobs are completed first;
        /// callers must then release their chunk leases before invoking Flush.
        /// </summary>
        public FlushResult Flush()
        {
            ThrowIfDisposed();
            CompleteDataDependencies();
            if (pool.ActiveLeaseCount != 0)
            {
                throw new InvalidOperationException(
                    "Cannot flush while chunk leases are active. Complete registered jobs and " +
                    "release every lease first.");
            }

            changes.Clear();
            changeIndices.Clear();
            dirtyChunks.Clear();
            dirtyIndices.Clear();

            var expandedWriteCount = GetExpandedWriteCount();
            EnsureWorkingCapacity(expandedWriteCount);
            var commandCount = commands.Length;

            for (var i = 0; i < commands.Length; i++)
            {
                var command = commands[i];
                switch (command.Type)
                {
                    case WorldCommandType.SetBlock:
                        ApplyWrite(command.Min, command.Block);
                        break;
                    case WorldCommandType.RemoveBlock:
                        ApplyWrite(command.Min, Block.Air);
                        break;
                    case WorldCommandType.FillRegion:
                        ApplyFill(command);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported command type: {command.Type}.");
                }
            }

            CollectDirtyChunks();
            commands.Clear();
            return new FlushResult(commandCount, changes.Length, dirtyChunks.Length);
        }

        public bool TryGetBlock(int3 worldPosition, out Block block)
        {
            ThrowIfDisposed();
            ChunkCoordinates.Split(worldPosition, out var chunkCoordinate, out var local);
            if (!chunks.TryGetValue(chunkCoordinate, out var handle))
            {
                block = Block.Air;
                return false;
            }

            return pool.TryGetBlock(handle, ChunkCoordinates.ToIndex(local), out block);
        }

        public Block GetBlockOrAir(int3 worldPosition)
        {
            return TryGetBlock(worldPosition, out var block) ? block : Block.Air;
        }

        public bool TryGetChunkHandle(int3 chunkCoordinate, out ChunkHandle handle)
        {
            ThrowIfDisposed();
            return chunks.TryGetValue(chunkCoordinate, out handle) && pool.IsValid(handle);
        }

        public bool IsChunkHandleValid(ChunkHandle handle)
        {
            ThrowIfDisposed();
            return pool.IsValid(handle);
        }

        public bool TryGetChunkCoordinate(ChunkHandle handle, out int3 chunkCoordinate)
        {
            ThrowIfDisposed();
            return pool.TryGetCoordinate(handle, out chunkCoordinate);
        }

        public bool TryGetChunkVersion(ChunkHandle handle, out ulong version)
        {
            ThrowIfDisposed();
            return pool.TryGetVersion(handle, out version);
        }

        /// <summary>
        /// Mesh/save work must present the captured version before its result is accepted.
        /// Stale or recycled handles are rejected as well.
        /// </summary>
        public bool IsChunkVersionCurrent(ChunkHandle handle, ulong expectedVersion)
        {
            ThrowIfDisposed();
            return pool.IsVersionCurrent(handle, expectedVersion);
        }

        public bool TryAcquireChunkLease(ChunkHandle handle, out ChunkLease lease)
        {
            ThrowIfDisposed();
            return pool.TryAcquireLease(handle, out lease);
        }

        public bool ReleaseChunkLease(in ChunkLease lease)
        {
            ThrowIfDisposed();
            return pool.ReleaseLease(lease);
        }

        /// <summary>
        /// Records a dependency for any scheduled job reading or writing a leased block slice.
        /// A mutating operation completes the combined dependency before touching native storage.
        /// </summary>
        public void RegisterDataDependency(JobHandle dependency)
        {
            ThrowIfDisposed();
            dataDependency = hasDataDependency
                ? JobHandle.CombineDependencies(dataDependency, dependency)
                : dependency;
            hasDataDependency = true;
        }

        public void CompleteDataDependencies()
        {
            ThrowIfDisposed();
            if (!hasDataDependency)
                return;

            dataDependency.Complete();
            dataDependency = default;
            hasDataDependency = false;
        }

        /// <summary>
        /// Explicitly unloads a chunk. It is rejected while any lease for the chunk exists.
        /// </summary>
        public bool TryUnloadChunk(int3 chunkCoordinate)
        {
            ThrowIfDisposed();
            CompleteDataDependencies();
            if (!chunks.TryGetValue(chunkCoordinate, out var handle))
                return false;
            if (!pool.Release(handle))
                return false;

            chunks.Remove(chunkCoordinate);
            return true;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            if (hasDataDependency)
                dataDependency.Complete();
            hasDataDependency = false;
            dataDependency = default;

            if (dirtyIndices.IsCreated)
                dirtyIndices.Dispose();
            if (dirtyChunks.IsCreated)
                dirtyChunks.Dispose();
            if (changeIndices.IsCreated)
                changeIndices.Dispose();
            if (changes.IsCreated)
                changes.Dispose();
            if (commands.IsCreated)
                commands.Dispose();
            if (chunks.IsCreated)
                chunks.Dispose();
            pool?.Dispose();
            pool = null;
            disposed = true;
        }

        private int GetExpandedWriteCount()
        {
            long total = 0;
            for (var i = 0; i < commands.Length; i++)
            {
                total += commands[i].ExpandedCellCount;
                if (total > int.MaxValue)
                    throw new InvalidOperationException("The queued command expansion exceeds Int32 capacity.");
            }

            return (int)total;
        }

        private void EnsureWorkingCapacity(int expandedWriteCount)
        {
            if (expandedWriteCount > changes.Capacity)
                changes.Capacity = expandedWriteCount;
            if (expandedWriteCount > changeIndices.Capacity)
                changeIndices.Capacity = expandedWriteCount;

            var dirtyCapacity = (int)Math.Min((long)expandedWriteCount * 4L, int.MaxValue);
            if (dirtyCapacity > dirtyChunks.Capacity)
                dirtyChunks.Capacity = dirtyCapacity;
            if (dirtyCapacity > dirtyIndices.Capacity)
                dirtyIndices.Capacity = dirtyCapacity;

            var requiredChunkLookup = pool.OccupiedCount + expandedWriteCount;
            if (requiredChunkLookup > chunks.Capacity)
                chunks.Capacity = requiredChunkLookup;
        }

        private void ApplyFill(WorldCommand command)
        {
            // X changes fastest to match the documented x + 16 * (y + 16 * z) layout.
            for (var z = (long)command.Min.z; z <= command.Max.z; z++)
            {
                for (var y = (long)command.Min.y; y <= command.Max.y; y++)
                {
                    for (var x = (long)command.Min.x; x <= command.Max.x; x++)
                        ApplyWrite(new int3((int)x, (int)y, (int)z), command.Block);
                }
            }
        }

        private void ApplyWrite(int3 worldPosition, Block finalBlock)
        {
            ChunkCoordinates.Split(worldPosition, out var chunkCoordinate, out var local);
            var loaded = chunks.TryGetValue(chunkCoordinate, out var handle);
            var originalForThisWrite = Block.Air;
            if (loaded)
                pool.TryGetBlock(handle, ChunkCoordinates.ToIndex(local), out originalForThisWrite);

            if (originalForThisWrite == finalBlock)
                return;
            if (!loaded && finalBlock.IsAir)
                return;

            if (!loaded)
            {
                handle = pool.Acquire(chunkCoordinate);
                if (!chunks.TryAdd(chunkCoordinate, handle))
                {
                    pool.Release(handle);
                    throw new InvalidOperationException("Chunk lookup rejected a newly allocated coordinate.");
                }
            }

            var localIndex = ChunkCoordinates.ToIndex(local);
            if (!pool.TrySetBlock(handle, localIndex, finalBlock))
                throw new InvalidOperationException("A chunk handle became stale during Flush.");

            if (changeIndices.TryGetValue(worldPosition, out var changeIndex))
            {
                var change = changes[changeIndex];
                change.Final = finalBlock;
                changes[changeIndex] = change;
            }
            else
            {
                var newIndex = changes.Length;
                changes.Add(new BlockChange(worldPosition, originalForThisWrite, finalBlock));
                changeIndices.Add(worldPosition, newIndex);
            }
        }

        private void CollectDirtyChunks()
        {
            for (var i = 0; i < changes.Length; i++)
            {
                var change = changes[i];
                if (change.Original == change.Final)
                    continue;

                ChunkCoordinates.Split(change.WorldPosition, out var chunkCoordinate, out var local);
                MarkDirty(chunkCoordinate, ChunkDirtyFlags.Data);

                if (local.x == 0)
                    MarkDirty(chunkCoordinate + new int3(-1, 0, 0), ChunkDirtyFlags.Neighbor);
                else if (local.x == ChunkCoordinates.Size - 1)
                    MarkDirty(chunkCoordinate + new int3(1, 0, 0), ChunkDirtyFlags.Neighbor);

                if (local.y == 0)
                    MarkDirty(chunkCoordinate + new int3(0, -1, 0), ChunkDirtyFlags.Neighbor);
                else if (local.y == ChunkCoordinates.Size - 1)
                    MarkDirty(chunkCoordinate + new int3(0, 1, 0), ChunkDirtyFlags.Neighbor);

                if (local.z == 0)
                    MarkDirty(chunkCoordinate + new int3(0, 0, -1), ChunkDirtyFlags.Neighbor);
                else if (local.z == ChunkCoordinates.Size - 1)
                    MarkDirty(chunkCoordinate + new int3(0, 0, 1), ChunkDirtyFlags.Neighbor);
            }
        }

        private void MarkDirty(int3 chunkCoordinate, ChunkDirtyFlags flags)
        {
            if (!chunks.TryGetValue(chunkCoordinate, out var handle) || !pool.IsValid(handle))
                return;

            if (dirtyIndices.TryGetValue(chunkCoordinate, out var index))
            {
                var dirty = dirtyChunks[index];
                dirty.Flags |= flags;
                dirtyChunks[index] = dirty;
                return;
            }

            var version = pool.IncrementVersion(handle);
            var newIndex = dirtyChunks.Length;
            dirtyChunks.Add(new DirtyChunk(chunkCoordinate, handle, version, flags));
            dirtyIndices.Add(chunkCoordinate, newIndex);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(WorldCore));
        }
    }
}
