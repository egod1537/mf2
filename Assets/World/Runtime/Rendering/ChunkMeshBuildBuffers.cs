using System;
using Unity.Collections;

namespace MF2.World.Rendering
{
    public struct ChunkMeshBuildBuffers : IDisposable
    {
        public NativeList<ChunkMeshVertex> Vertices;
        public NativeList<uint> OpaqueIndices;
        public NativeList<uint> CutoutIndices;
        public NativeList<uint> TransparentIndices;
        public NativeList<uint> WaterIndices;
        public NativeList<uint> CollisionIndices;

        public ChunkMeshBuildBuffers(int initialFaceCapacity, Allocator allocator)
        {
            Vertices = new NativeList<ChunkMeshVertex>(initialFaceCapacity * 4, allocator);
            OpaqueIndices = new NativeList<uint>(initialFaceCapacity * 6, allocator);
            CutoutIndices = new NativeList<uint>(initialFaceCapacity * 6, allocator);
            TransparentIndices = new NativeList<uint>(initialFaceCapacity * 6, allocator);
            WaterIndices = new NativeList<uint>(initialFaceCapacity * 6, allocator);
            CollisionIndices = new NativeList<uint>(initialFaceCapacity * 6, allocator);
        }

        public int FaceCount => Vertices.IsCreated ? Vertices.Length / 4 : 0;

        public void Dispose()
        {
            if (CollisionIndices.IsCreated)
                CollisionIndices.Dispose();
            if (WaterIndices.IsCreated)
                WaterIndices.Dispose();
            if (TransparentIndices.IsCreated)
                TransparentIndices.Dispose();
            if (CutoutIndices.IsCreated)
                CutoutIndices.Dispose();
            if (OpaqueIndices.IsCreated)
                OpaqueIndices.Dispose();
            if (Vertices.IsCreated)
                Vertices.Dispose();
        }
    }
}
