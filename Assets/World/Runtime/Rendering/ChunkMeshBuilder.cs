using MF2.World.Core;
using Unity.Collections;
using Unity.Jobs;

namespace MF2.World.Rendering
{
    public static class ChunkMeshBuilder
    {
        public static bool TrySchedule(
            WorldCore world,
            ChunkHandle handle,
            BlockRenderTable renderTable,
            out ChunkMeshBuildRequest request,
            Allocator allocator = Allocator.TempJob)
        {
            request = null;
            if (world == null || renderTable == null ||
                !ChunkMeshingSnapshotBuilder.TryGather(world, handle, out var snapshot, allocator))
            {
                return false;
            }

            var buffers = new ChunkMeshBuildBuffers(256, allocator);
            try
            {
                var job = new ChunkMeshBuildJob
                {
                    Blocks = snapshot.Blocks,
                    NeighborBorders = snapshot.NeighborBorders,
                    RenderDefinitions = renderTable.Definitions,
                    RenderLookup = renderTable.IdToIndex,
                    Vertices = buffers.Vertices,
                    OpaqueIndices = buffers.OpaqueIndices,
                    CutoutIndices = buffers.CutoutIndices,
                    TransparentIndices = buffers.TransparentIndices,
                    WaterIndices = buffers.WaterIndices,
                    CollisionIndices = buffers.CollisionIndices
                };

                request = new ChunkMeshBuildRequest(snapshot, buffers, job.Schedule());
                snapshot = default;
                buffers = default;
                return true;
            }
            finally
            {
                buffers.Dispose();
                snapshot.Dispose();
            }
        }
    }
}
