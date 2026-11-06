using System;
using Unity.Mathematics;

namespace MF2.World.Core
{
    public struct BlockChange
    {
        public int3 WorldPosition;
        public Block Original;
        public Block Final;

        internal BlockChange(int3 worldPosition, Block original, Block final)
        {
            WorldPosition = worldPosition;
            Original = original;
            Final = final;
        }
    }

    [Flags]
    public enum ChunkDirtyFlags : byte
    {
        None = 0,
        Data = 1 << 0,
        Neighbor = 1 << 1
    }

    public struct DirtyChunk
    {
        public int3 ChunkCoordinate;
        public ChunkHandle Handle;
        public ulong Version;
        public ChunkDirtyFlags Flags;

        internal DirtyChunk(
            int3 chunkCoordinate,
            ChunkHandle handle,
            ulong version,
            ChunkDirtyFlags flags)
        {
            ChunkCoordinate = chunkCoordinate;
            Handle = handle;
            Version = version;
            Flags = flags;
        }
    }

    public readonly struct FlushResult
    {
        public readonly int CommandsApplied;
        public readonly int ChangedCells;
        public readonly int DirtyChunks;

        internal FlushResult(int commandsApplied, int changedCells, int dirtyChunks)
        {
            CommandsApplied = commandsApplied;
            ChangedCells = changedCells;
            DirtyChunks = dirtyChunks;
        }
    }
}
