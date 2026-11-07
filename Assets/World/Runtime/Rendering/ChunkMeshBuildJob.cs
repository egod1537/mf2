using MF2.World.Core;
using MF2.World.Registry;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace MF2.World.Rendering
{
    /// <summary>
    /// Cube-only fast path. This job deliberately contains no UnityEngine or Mesh API usage.
    /// Variant geometry can be dispatched by a later job without changing the render table format.
    /// </summary>
    [BurstCompile]
    internal struct ChunkMeshBuildJob : IJob
    {
        [ReadOnly] public NativeArray<Block> Blocks;
        [ReadOnly] public NativeArray<Block> NeighborBorders;
        [ReadOnly] public NativeArray<BlockRenderDefinition>.ReadOnly RenderDefinitions;
        [ReadOnly] public NativeParallelHashMap<uint, int>.ReadOnly RenderLookup;

        public NativeList<ChunkMeshVertex> Vertices;
        public NativeList<uint> OpaqueIndices;
        public NativeList<uint> CutoutIndices;
        public NativeList<uint> TransparentIndices;
        public NativeList<uint> WaterIndices;
        public NativeList<uint> CollisionIndices;

        public void Execute()
        {
            for (var index = 0; index < ChunkCoordinates.Volume; index++)
            {
                var block = Blocks[index];
                if (block.IsAir || !RenderLookup.TryGetValue(block.Id, out var renderIndex))
                    continue;

                var definition = RenderDefinitions[renderIndex];
                if (definition.GeometryKind != BlockGeometryKind.Cube)
                    continue;

                var local = FromIndex(index);
                for (var faceIndex = 0; faceIndex < 6; faceIndex++)
                {
                    var face = (BlockFace)faceIndex;
                    var neighbor = GetNeighbor(local, face);
                    if (!ShouldRenderFace(definition.TraitsKind, neighbor))
                        continue;

                    AddFace(local, face, GetFace(definition, face), definition.TraitsKind);
                }
            }
        }

        private bool ShouldRenderFace(BlockTraitsKind sourceKind, Block neighbor)
        {
            if (neighbor.IsAir)
                return true;

            var neighborKind = BlockTraitsKind.SolidOpaque;
            if (RenderLookup.TryGetValue(neighbor.Id, out var neighborIndex))
                neighborKind = RenderDefinitions[neighborIndex].TraitsKind;

            switch (sourceKind)
            {
                case BlockTraitsKind.Liquid:
                    return neighborKind == BlockTraitsKind.NonSolidNonOccluding;
                case BlockTraitsKind.NonSolidNonOccluding:
                    return neighborKind != BlockTraitsKind.NonSolidNonOccluding;
                default:
                    return neighborKind != BlockTraitsKind.SolidOpaque;
            }
        }

        private Block GetNeighbor(int3 local, BlockFace face)
        {
            switch (face)
            {
                case BlockFace.North:
                    return local.z > 0
                        ? Blocks[ToIndex(local.x, local.y, local.z - 1)]
                        : GetBorder(face, local.x, local.y);
                case BlockFace.South:
                    return local.z < ChunkCoordinates.Size - 1
                        ? Blocks[ToIndex(local.x, local.y, local.z + 1)]
                        : GetBorder(face, local.x, local.y);
                case BlockFace.East:
                    return local.x < ChunkCoordinates.Size - 1
                        ? Blocks[ToIndex(local.x + 1, local.y, local.z)]
                        : GetBorder(face, local.z, local.y);
                case BlockFace.West:
                    return local.x > 0
                        ? Blocks[ToIndex(local.x - 1, local.y, local.z)]
                        : GetBorder(face, local.z, local.y);
                case BlockFace.Up:
                    return local.y < ChunkCoordinates.Size - 1
                        ? Blocks[ToIndex(local.x, local.y + 1, local.z)]
                        : GetBorder(face, local.x, local.z);
                default:
                    return local.y > 0
                        ? Blocks[ToIndex(local.x, local.y - 1, local.z)]
                        : GetBorder(face, local.x, local.z);
            }
        }

        private Block GetBorder(BlockFace face, int u, int v)
        {
            return NeighborBorders[
                (int)face * ChunkMeshingSnapshot.BorderArea +
                u + ChunkCoordinates.Size * v];
        }

        private void AddFace(
            int3 local,
            BlockFace face,
            BlockFaceRenderDefinition definition,
            BlockTraitsKind traitsKind)
        {
            var origin = new float3(local.x, local.y, local.z);
            float3 normal;
            float3 p0;
            float3 p1;
            float3 p2;
            float3 p3;
            const float half = 0.5f;

            switch (face)
            {
                case BlockFace.North:
                    normal = new float3(0f, 0f, -1f);
                    p0 = new float3(-half, -half, -half);
                    p1 = new float3(-half, half, -half);
                    p2 = new float3(half, half, -half);
                    p3 = new float3(half, -half, -half);
                    break;
                case BlockFace.South:
                    normal = new float3(0f, 0f, 1f);
                    p0 = new float3(half, -half, half);
                    p1 = new float3(half, half, half);
                    p2 = new float3(-half, half, half);
                    p3 = new float3(-half, -half, half);
                    break;
                case BlockFace.East:
                    normal = new float3(1f, 0f, 0f);
                    p0 = new float3(half, -half, -half);
                    p1 = new float3(half, half, -half);
                    p2 = new float3(half, half, half);
                    p3 = new float3(half, -half, half);
                    break;
                case BlockFace.West:
                    normal = new float3(-1f, 0f, 0f);
                    p0 = new float3(-half, -half, half);
                    p1 = new float3(-half, half, half);
                    p2 = new float3(-half, half, -half);
                    p3 = new float3(-half, -half, -half);
                    break;
                case BlockFace.Up:
                    normal = new float3(0f, 1f, 0f);
                    p0 = new float3(-half, half, -half);
                    p1 = new float3(-half, half, half);
                    p2 = new float3(half, half, half);
                    p3 = new float3(half, half, -half);
                    break;
                default:
                    normal = new float3(0f, -1f, 0f);
                    p0 = new float3(-half, -half, half);
                    p1 = new float3(-half, -half, -half);
                    p2 = new float3(half, -half, -half);
                    p3 = new float3(half, -half, half);
                    break;
            }

            var firstVertex = (uint)Vertices.Length;
            Vertices.Add(new ChunkMeshVertex(origin + p0, normal, definition.Uv0));
            Vertices.Add(new ChunkMeshVertex(origin + p1, normal, definition.Uv1));
            Vertices.Add(new ChunkMeshVertex(origin + p2, normal, definition.Uv2));
            Vertices.Add(new ChunkMeshVertex(origin + p3, normal, definition.Uv3));
            AddIndex(definition.Pass, firstVertex);
            AddIndex(definition.Pass, firstVertex + 1);
            AddIndex(definition.Pass, firstVertex + 2);
            AddIndex(definition.Pass, firstVertex);
            AddIndex(definition.Pass, firstVertex + 2);
            AddIndex(definition.Pass, firstVertex + 3);

            if (traitsKind == BlockTraitsKind.SolidOpaque)
            {
                CollisionIndices.Add(firstVertex);
                CollisionIndices.Add(firstVertex + 1);
                CollisionIndices.Add(firstVertex + 2);
                CollisionIndices.Add(firstVertex);
                CollisionIndices.Add(firstVertex + 2);
                CollisionIndices.Add(firstVertex + 3);
            }
        }

        private void AddIndex(BlockRenderPass pass, uint index)
        {
            switch (pass)
            {
                case BlockRenderPass.Cutout:
                    CutoutIndices.Add(index);
                    break;
                case BlockRenderPass.Transparent:
                    TransparentIndices.Add(index);
                    break;
                case BlockRenderPass.Water:
                    WaterIndices.Add(index);
                    break;
                default:
                    OpaqueIndices.Add(index);
                    break;
            }
        }

        private static int ToIndex(int x, int y, int z)
        {
            return x + ChunkCoordinates.Size * (y + ChunkCoordinates.Size * z);
        }

        private static int3 FromIndex(int index)
        {
            var x = index % ChunkCoordinates.Size;
            var yz = index / ChunkCoordinates.Size;
            return new int3(x, yz % ChunkCoordinates.Size, yz / ChunkCoordinates.Size);
        }

        private static BlockFaceRenderDefinition GetFace(
            BlockRenderDefinition definition,
            BlockFace face)
        {
            switch (face)
            {
                case BlockFace.North: return definition.North;
                case BlockFace.South: return definition.South;
                case BlockFace.East: return definition.East;
                case BlockFace.West: return definition.West;
                case BlockFace.Up: return definition.Up;
                default: return definition.Down;
            }
        }
    }
}
