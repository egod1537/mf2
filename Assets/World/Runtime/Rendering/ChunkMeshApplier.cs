using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MF2.World.Rendering
{
    /// <summary>Main-thread bridge from native job output to Unity MeshData.</summary>
    public static class ChunkMeshApplier
    {
        private static readonly Bounds ChunkBounds =
            new Bounds(new Vector3(7.5f, 7.5f, 7.5f), Vector3.one * 16f);

        public static void Apply(
            Mesh mesh,
            NativeArray<ChunkMeshVertex> vertices,
            NativeArray<uint> opaqueIndices,
            NativeArray<uint> cutoutIndices,
            NativeArray<uint> transparentIndices,
            NativeArray<uint> waterIndices)
        {
            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            var meshDataDisposed = false;
            try
            {
                var meshData = meshDataArray[0];
                meshData.SetVertexBufferParams(
                    vertices.Length,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
                meshData.GetVertexData<ChunkMeshVertex>().CopyFrom(vertices);

                var indexCount = opaqueIndices.Length + cutoutIndices.Length +
                                 transparentIndices.Length + waterIndices.Length;
                meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                var indices = meshData.GetIndexData<uint>();
                var cursor = CopyIndices(opaqueIndices, indices, 0);
                cursor = CopyIndices(cutoutIndices, indices, cursor);
                cursor = CopyIndices(transparentIndices, indices, cursor);
                CopyIndices(waterIndices, indices, cursor);

                meshData.subMeshCount = 4;
                var opaqueStart = 0;
                var cutoutStart = opaqueIndices.Length;
                var transparentStart = cutoutStart + cutoutIndices.Length;
                var waterStart = transparentStart + transparentIndices.Length;
                SetSubMesh(meshData, 0, opaqueStart, opaqueIndices.Length);
                SetSubMesh(meshData, 1, cutoutStart, cutoutIndices.Length);
                SetSubMesh(meshData, 2, transparentStart, transparentIndices.Length);
                SetSubMesh(meshData, 3, waterStart, waterIndices.Length);

                Mesh.ApplyAndDisposeWritableMeshData(
                    meshDataArray,
                    mesh,
                    MeshUpdateFlags.DontRecalculateBounds |
                    MeshUpdateFlags.DontValidateIndices |
                    MeshUpdateFlags.DontNotifyMeshUsers);
                meshDataDisposed = true;
                mesh.bounds = ChunkBounds;
            }
            catch
            {
                if (!meshDataDisposed)
                    meshDataArray.Dispose();
                throw;
            }
        }

        public static void ApplyCollider(
            Mesh mesh,
            NativeArray<ChunkMeshVertex> vertices,
            NativeArray<uint> collisionIndices)
        {
            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            var meshDataDisposed = false;
            try
            {
                var meshData = meshDataArray[0];
                meshData.SetVertexBufferParams(
                    vertices.Length,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
                meshData.GetVertexData<ChunkMeshVertex>().CopyFrom(vertices);
                meshData.SetIndexBufferParams(collisionIndices.Length, IndexFormat.UInt32);
                meshData.GetIndexData<uint>().CopyFrom(collisionIndices);
                meshData.subMeshCount = 1;
                SetSubMesh(meshData, 0, 0, collisionIndices.Length);

                Mesh.ApplyAndDisposeWritableMeshData(
                    meshDataArray,
                    mesh,
                    MeshUpdateFlags.DontRecalculateBounds |
                    MeshUpdateFlags.DontValidateIndices |
                    MeshUpdateFlags.DontNotifyMeshUsers);
                meshDataDisposed = true;
                mesh.bounds = ChunkBounds;
            }
            catch
            {
                if (!meshDataDisposed)
                    meshDataArray.Dispose();
                throw;
            }
        }

        private static int CopyIndices(
            NativeArray<uint> source,
            NativeArray<uint> destination,
            int destinationStart)
        {
            for (var i = 0; i < source.Length; i++)
                destination[destinationStart + i] = source[i];
            return destinationStart + source.Length;
        }

        private static void SetSubMesh(Mesh.MeshData meshData, int subMesh, int start, int count)
        {
            meshData.SetSubMesh(
                subMesh,
                new SubMeshDescriptor(start, count, MeshTopology.Triangles)
                {
                    bounds = ChunkBounds,
                    firstVertex = 0,
                    vertexCount = meshData.vertexCount
                },
                MeshUpdateFlags.DontRecalculateBounds |
                MeshUpdateFlags.DontValidateIndices |
                MeshUpdateFlags.DontNotifyMeshUsers);
        }
    }
}
