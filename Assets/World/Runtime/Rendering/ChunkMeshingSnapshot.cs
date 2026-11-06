using System;
using MF2.World.Core;
using Unity.Collections;
using Unity.Mathematics;

namespace MF2.World.Rendering
{
    /// <summary>
    /// Immutable input copied before a mesh job is scheduled. Neighbor borders are ordered by
    /// BlockFace and contain 16x16 cells each. Missing chunks are represented by Air.
    /// </summary>
    public struct ChunkMeshingSnapshot : IDisposable
    {
        public const int BorderArea = ChunkCoordinates.Area;
        public const int BorderCount = 6 * BorderArea;

        public ChunkHandle Handle;
        public int3 ChunkCoordinate;
        public ulong Version;
        public NativeArray<Block> Blocks;
        public NativeArray<Block> NeighborBorders;

        public bool IsCreated => Blocks.IsCreated && NeighborBorders.IsCreated;

        public bool IsCurrent(WorldCore world)
        {
            return world != null && world.IsChunkVersionCurrent(Handle, Version);
        }

        public void Dispose()
        {
            if (NeighborBorders.IsCreated)
                NeighborBorders.Dispose();
            if (Blocks.IsCreated)
                Blocks.Dispose();
        }
    }
}
