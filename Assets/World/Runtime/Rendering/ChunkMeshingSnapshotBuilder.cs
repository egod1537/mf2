using MF2.World.Core;
using Unity.Collections;
using Unity.Mathematics;

namespace MF2.World.Rendering
{
    public static class ChunkMeshingSnapshotBuilder
    {
        private static readonly int3[] NeighborOffsets =
        {
            new int3(0, 0, -1),
            new int3(0, 0, 1),
            new int3(1, 0, 0),
            new int3(-1, 0, 0),
            new int3(0, 1, 0),
            new int3(0, -1, 0)
        };

        public static bool TryGather(
            WorldCore world,
            ChunkHandle handle,
            out ChunkMeshingSnapshot snapshot,
            Allocator allocator = Allocator.TempJob)
        {
            snapshot = default;
            if (world == null || !world.TryGetChunkCoordinate(handle, out var chunkCoordinate))
                return false;
            if (!world.TryGetChunkVersion(handle, out var version))
                return false;

            var blocks = new NativeArray<Block>(
                ChunkCoordinates.Volume,
                allocator,
                NativeArrayOptions.UninitializedMemory);
            var borders = new NativeArray<Block>(
                ChunkMeshingSnapshot.BorderCount,
                allocator,
                NativeArrayOptions.ClearMemory);

            try
            {
                if (!world.TryAcquireChunkLease(handle, out var centerLease))
                    return false;

                try
                {
                    for (var i = 0; i < ChunkCoordinates.Volume; i++)
                        blocks[i] = centerLease.GetBlock(i);
                }
                finally
                {
                    world.ReleaseChunkLease(centerLease);
                }

                for (var faceIndex = 0; faceIndex < NeighborOffsets.Length; faceIndex++)
                {
                    var neighborCoordinate = chunkCoordinate + NeighborOffsets[faceIndex];
                    if (!world.TryGetChunkHandle(neighborCoordinate, out var neighborHandle) ||
                        !world.TryAcquireChunkLease(neighborHandle, out var neighborLease))
                    {
                        continue;
                    }

                    try
                    {
                        CopyFacingBorder((BlockFace)faceIndex, neighborLease, borders);
                    }
                    finally
                    {
                        world.ReleaseChunkLease(neighborLease);
                    }
                }

                if (!world.IsChunkVersionCurrent(handle, version))
                    return false;

                snapshot = new ChunkMeshingSnapshot
                {
                    Handle = handle,
                    ChunkCoordinate = chunkCoordinate,
                    Version = version,
                    Blocks = blocks,
                    NeighborBorders = borders
                };
                blocks = default;
                borders = default;
                return true;
            }
            finally
            {
                if (borders.IsCreated)
                    borders.Dispose();
                if (blocks.IsCreated)
                    blocks.Dispose();
            }
        }

        private static void CopyFacingBorder(
            BlockFace face,
            ChunkLease neighbor,
            NativeArray<Block> destination)
        {
            var baseIndex = (int)face * ChunkMeshingSnapshot.BorderArea;
            for (var v = 0; v < ChunkCoordinates.Size; v++)
            {
                for (var u = 0; u < ChunkCoordinates.Size; u++)
                {
                    int3 local;
                    switch (face)
                    {
                        case BlockFace.North:
                            local = new int3(u, v, ChunkCoordinates.Size - 1);
                            break;
                        case BlockFace.South:
                            local = new int3(u, v, 0);
                            break;
                        case BlockFace.East:
                            local = new int3(0, v, u);
                            break;
                        case BlockFace.West:
                            local = new int3(ChunkCoordinates.Size - 1, v, u);
                            break;
                        case BlockFace.Up:
                            local = new int3(u, 0, v);
                            break;
                        default:
                            local = new int3(u, ChunkCoordinates.Size - 1, v);
                            break;
                    }

                    destination[baseIndex + u + ChunkCoordinates.Size * v] =
                        neighbor.GetBlock(ChunkCoordinates.ToIndex(local));
                }
            }
        }
    }
}
