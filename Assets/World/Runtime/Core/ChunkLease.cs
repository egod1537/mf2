using System;
using Unity.Collections;
using Unity.Mathematics;

namespace MF2.World.Core
{
    /// <summary>
    /// A fixed-size block view retained until ReleaseChunkLease is called.
    /// The token makes releasing a copied lease twice harmless (the second release is rejected).
    /// </summary>
    public readonly struct ChunkLease
    {
        public readonly ChunkHandle Handle;
        public readonly int3 ChunkCoordinate;
        public readonly NativeArray<Block>.ReadOnly Storage;
        public readonly int StartIndex;

        internal readonly ulong Token;

        internal ChunkLease(
            ChunkHandle handle,
            int3 chunkCoordinate,
            NativeArray<Block>.ReadOnly storage,
            int startIndex,
            ulong token)
        {
            Handle = handle;
            ChunkCoordinate = chunkCoordinate;
            Storage = storage;
            StartIndex = startIndex;
            Token = token;
        }

        public int Length => ChunkCoordinates.Volume;

        public bool IsValid =>
            Handle.IsValid &&
            Token != 0 &&
            Storage.IsCreated &&
            StartIndex >= 0 &&
            StartIndex <= Storage.Length - ChunkCoordinates.Volume;

        public Block GetBlock(int localIndex)
        {
            if ((uint)localIndex >= ChunkCoordinates.Volume)
                throw new ArgumentOutOfRangeException(nameof(localIndex));
            return Storage[StartIndex + localIndex];
        }
    }
}
