using System;
using System.Runtime.InteropServices;
using MF2.World.Registry;
using Unity.Mathematics;

namespace MF2.World.Rendering
{
    public enum BlockFace : byte
    {
        North = 0,
        South = 1,
        East = 2,
        West = 3,
        Up = 4,
        Down = 5
    }

    public enum BlockRenderPass : byte
    {
        Opaque = 0,
        Cutout = 1,
        Transparent = 2,
        Water = 3
    }

    public enum BlockGeometryKind : byte
    {
        Cube = 0,
        Variant = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BlockFaceRenderDefinition
    {
        public float2 Uv0;
        public float2 Uv1;
        public float2 Uv2;
        public float2 Uv3;
        public BlockRenderPass Pass;

        public BlockFaceRenderDefinition(
            float2 uv0,
            float2 uv1,
            float2 uv2,
            float2 uv3,
            BlockRenderPass pass)
        {
            Uv0 = uv0;
            Uv1 = uv1;
            Uv2 = uv2;
            Uv3 = uv3;
            Pass = pass;
        }

        public readonly float2 GetUv(int corner)
        {
            switch (corner)
            {
                case 0: return Uv0;
                case 1: return Uv1;
                case 2: return Uv2;
                case 3: return Uv3;
                default: throw new ArgumentOutOfRangeException(nameof(corner));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BlockRenderDefinition
    {
        public uint Id;
        public BlockGeometryKind GeometryKind;
        public BlockTraitsKind TraitsKind;
        public BlockFaceRenderDefinition North;
        public BlockFaceRenderDefinition South;
        public BlockFaceRenderDefinition East;
        public BlockFaceRenderDefinition West;
        public BlockFaceRenderDefinition Up;
        public BlockFaceRenderDefinition Down;

        public readonly BlockFaceRenderDefinition GetFace(BlockFace face)
        {
            switch (face)
            {
                case BlockFace.North: return North;
                case BlockFace.South: return South;
                case BlockFace.East: return East;
                case BlockFace.West: return West;
                case BlockFace.Up: return Up;
                case BlockFace.Down: return Down;
                default: throw new ArgumentOutOfRangeException(nameof(face));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkMeshVertex
    {
        public float3 Position;
        public float3 Normal;
        public float2 Uv;

        public ChunkMeshVertex(float3 position, float3 normal, float2 uv)
        {
            Position = position;
            Normal = normal;
            Uv = uv;
        }
    }
}
