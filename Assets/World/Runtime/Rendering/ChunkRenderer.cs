using System;
using System.Collections.Generic;
using MF2.World.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MF2.World.Rendering
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class ChunkRenderer : MonoBehaviour
    {
        private const int BaseRenderSubmeshCount = 4;
        public const int OcclusionSubmeshIndex = BaseRenderSubmeshCount;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;
        private Mesh mesh;
        private Mesh colliderMesh;
        private Mesh occlusionBackingMesh;
        private MeshRenderer occlusionBackingRenderer;
        private ChunkReadiness readiness;
        private readonly int[][] baseIndices = new int[BaseRenderSubmeshCount][];
        private readonly List<int>[] routedIndices =
        {
            new List<int>(),
            new List<int>(),
            new List<int>(),
            new List<int>()
        };
        private readonly List<int> occlusionIndices = new List<int>();
        private readonly List<Vector3> vertexPositions = new List<Vector3>();
        private readonly List<Vector3> vertexNormals = new List<Vector3>();
        private readonly List<Vector2> vertexUvs = new List<Vector2>();
        private readonly HashSet<int> visibleBlockFaces = new HashSet<int>();
        private readonly Dictionary<int3, Vector2[]> blockFaceUvs =
            new Dictionary<int3, Vector2[]>();
        private readonly List<Vector3> backingVertices = new List<Vector3>();
        private readonly List<Vector3> backingNormals = new List<Vector3>();
        private readonly List<Vector2> backingUvs = new List<Vector2>();
        private readonly List<int> backingIndices = new List<int>();
        private readonly HashSet<int3> ditheredBlocks = new HashSet<int3>();
        private bool hasBaseGeometry;

        public int3 ChunkCoordinate { get; private set; }
        public ChunkHandle AppliedHandle { get; private set; }
        public ulong AppliedVersion { get; private set; }
        public bool RenderingEnabled => meshRenderer != null && meshRenderer.enabled;

        public bool IsCollisionReadyFor(ChunkHandle handle, ulong version) =>
            AppliedHandle == handle &&
            AppliedVersion == version &&
            readiness.Coordinate.Equals(ChunkCoordinate) &&
            readiness.DataVersion == version &&
            readiness.IsCollisionReady;

        public Mesh Mesh
        {
            get
            {
                EnsureInitialized();
                return mesh;
            }
        }

        public Mesh ColliderMesh
        {
            get
            {
                EnsureInitialized();
                return colliderMesh;
            }
        }

        public Mesh OcclusionBackingMesh
        {
            get
            {
                EnsureInitialized();
                return occlusionBackingMesh;
            }
        }

        public void Configure(int3 chunkCoordinate, Material[] sharedMaterials)
        {
            EnsureInitialized();
            ChunkCoordinate = chunkCoordinate;
            transform.localPosition = new Vector3(
                chunkCoordinate.x * (float)ChunkCoordinates.Size,
                chunkCoordinate.y * (float)ChunkCoordinates.Size,
                chunkCoordinate.z * (float)ChunkCoordinates.Size);
            meshRenderer.sharedMaterials = sharedMaterials;
            occlusionBackingRenderer.sharedMaterial =
                sharedMaterials != null && sharedMaterials.Length > OcclusionSubmeshIndex
                    ? sharedMaterials[OcclusionSubmeshIndex]
                    : null;
            gameObject.name = $"Chunk ({chunkCoordinate.x}, {chunkCoordinate.y}, {chunkCoordinate.z})";
        }

        public void SetRenderingEnabled(bool enabled)
        {
            EnsureInitialized();
            meshRenderer.enabled = enabled;
            occlusionBackingRenderer.enabled = enabled && backingIndices.Count > 0;
        }

        public bool Apply(WorldCore world, ChunkMeshBuildRequest request)
        {
            EnsureInitialized();
            if (!request.TryApply(world, mesh, colliderMesh))
                return false;

            // Reassigning through null makes PhysX recook the exact accepted mesh version.
            if (AppliedVersion != 0 && AppliedHandle != request.Handle)
                readiness = default;
            readiness.MarkDataReady(request.ChunkCoordinate, request.Version);
            CaptureBaseGeometry();
            ApplyDitheredSubmesh();
            meshCollider.sharedMesh = null;
            var hasCollision = request.CollisionIndices.Length > 0;
            meshCollider.enabled = hasCollision;
            if (hasCollision)
                meshCollider.sharedMesh = colliderMesh;

            AppliedHandle = request.Handle;
            AppliedVersion = request.Version;
            readiness.TryMarkCollisionReady(request.Version);
            readiness.TryMarkVisualReady(request.Version);
            return true;
        }

        public void Clear()
        {
            if (meshCollider != null)
                meshCollider.sharedMesh = null;
            if (mesh != null)
                mesh.Clear(false);
            if (colliderMesh != null)
                colliderMesh.Clear(false);
            if (occlusionBackingMesh != null)
                occlusionBackingMesh.Clear(false);
            if (occlusionBackingRenderer != null)
                occlusionBackingRenderer.enabled = false;
            if (meshCollider != null)
                meshCollider.enabled = false;
            ClearCachedGeometry();
            AppliedHandle = default;
            AppliedVersion = 0;
            readiness = default;
        }

        public void SetDitheredBlocks(IReadOnlyCollection<int3> localBlocks)
        {
            if (localBlocks == null)
                throw new ArgumentNullException(nameof(localBlocks));
            if (ditheredBlocks.SetEquals(localBlocks))
                return;

            ditheredBlocks.Clear();
            foreach (var block in localBlocks)
                ditheredBlocks.Add(block);
            ApplyDitheredSubmesh();
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            DestroyOwnedMesh(mesh);
            DestroyOwnedMesh(colliderMesh);
            DestroyOwnedMesh(occlusionBackingMesh);
            mesh = null;
            colliderMesh = null;
            occlusionBackingMesh = null;
        }

        private void EnsureInitialized()
        {
            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null)
                meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            if (meshCollider == null)
            {
                meshCollider = GetComponent<MeshCollider>();
                meshCollider.convex = false;
            }
            if (mesh != null)
                return;

            mesh = new Mesh
            {
                name = "Chunk Mesh",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.DontSave
            };
            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;

            colliderMesh = new Mesh
            {
                name = "Chunk Collider Mesh",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.DontSave
            };
            colliderMesh.MarkDynamic();

            var backingObject = new GameObject("Occlusion Backing")
            {
                hideFlags = HideFlags.DontSave
            };
            backingObject.transform.SetParent(transform, false);
            var backingFilter = backingObject.AddComponent<MeshFilter>();
            occlusionBackingRenderer = backingObject.AddComponent<MeshRenderer>();
            occlusionBackingRenderer.shadowCastingMode = ShadowCastingMode.Off;
            occlusionBackingRenderer.receiveShadows = false;
            occlusionBackingRenderer.enabled = false;
            occlusionBackingMesh = new Mesh
            {
                name = "Chunk Occlusion Backing Mesh",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.DontSave
            };
            occlusionBackingMesh.MarkDynamic();
            backingFilter.sharedMesh = occlusionBackingMesh;
        }

        private void CaptureBaseGeometry()
        {
            vertexPositions.Clear();
            vertexNormals.Clear();
            mesh.GetVertices(vertexPositions);
            mesh.GetNormals(vertexNormals);
            mesh.GetUVs(0, vertexUvs);
            visibleBlockFaces.Clear();
            blockFaceUvs.Clear();
            for (var vertex = 0; vertex + 3 < vertexPositions.Count; vertex += 4)
            {
                var block = ResolveTriangleBlock(vertex, vertex + 1, vertex + 2);
                var face = ResolveFace(vertexNormals[vertex]);
                visibleBlockFaces.Add(EncodeFace(block, face));
                if (!blockFaceUvs.ContainsKey(block))
                {
                    blockFaceUvs.Add(block, new[]
                    {
                        vertexUvs[vertex],
                        vertexUvs[vertex + 1],
                        vertexUvs[vertex + 2],
                        vertexUvs[vertex + 3]
                    });
                }
            }
            for (var submesh = 0; submesh < BaseRenderSubmeshCount; submesh++)
                baseIndices[submesh] = mesh.GetIndices(submesh);
            hasBaseGeometry = true;
        }

        private void ApplyDitheredSubmesh()
        {
            if (!hasBaseGeometry ||
                meshRenderer == null ||
                meshRenderer.sharedMaterials.Length <= OcclusionSubmeshIndex)
            {
                return;
            }

            occlusionIndices.Clear();
            for (var submesh = 0; submesh < BaseRenderSubmeshCount; submesh++)
            {
                var routed = routedIndices[submesh];
                routed.Clear();
                var source = baseIndices[submesh] ?? Array.Empty<int>();
                for (var index = 0; index + 2 < source.Length; index += 3)
                {
                    var destination = ditheredBlocks.Contains(ResolveTriangleBlock(
                        source[index],
                        source[index + 1],
                        source[index + 2]))
                        ? occlusionIndices
                        : routed;
                    destination.Add(source[index]);
                    destination.Add(source[index + 1]);
                    destination.Add(source[index + 2]);
                }
            }

            mesh.subMeshCount = OcclusionSubmeshIndex + 1;
            for (var submesh = 0; submesh < BaseRenderSubmeshCount; submesh++)
                mesh.SetIndices(routedIndices[submesh], MeshTopology.Triangles, submesh, false);
            mesh.SetIndices(
                occlusionIndices,
                MeshTopology.Triangles,
                OcclusionSubmeshIndex,
                false);
            RebuildOcclusionBacking();
        }

        private void RebuildOcclusionBacking()
        {
            backingVertices.Clear();
            backingNormals.Clear();
            backingUvs.Clear();
            backingIndices.Clear();

            foreach (var block in ditheredBlocks)
            {
                if (!blockFaceUvs.TryGetValue(block, out var uv))
                    continue;

                for (var face = 0; face < 6; face++)
                {
                    if (visibleBlockFaces.Contains(EncodeFace(block, face)) ||
                        ditheredBlocks.Contains(block + FaceOffset(face)))
                    {
                        continue;
                    }
                    AddBackingFace(block, face, uv);
                }
            }

            occlusionBackingMesh.Clear(false);
            if (backingIndices.Count == 0)
            {
                occlusionBackingRenderer.enabled = false;
                return;
            }

            occlusionBackingMesh.SetVertices(backingVertices);
            occlusionBackingMesh.SetNormals(backingNormals);
            occlusionBackingMesh.SetUVs(0, backingUvs);
            occlusionBackingMesh.SetTriangles(backingIndices, 0, false);
            occlusionBackingMesh.bounds = mesh.bounds;
            occlusionBackingRenderer.enabled = meshRenderer.enabled;
        }

        private void AddBackingFace(int3 block, int face, Vector2[] uv)
        {
            var center = new Vector3(block.x, block.y, block.z);
            var normal = FaceNormal(face);
            GetFaceCorners(face, out var p0, out var p1, out var p2, out var p3);
            var first = backingVertices.Count;
            backingVertices.Add(center + p0);
            backingVertices.Add(center + p1);
            backingVertices.Add(center + p2);
            backingVertices.Add(center + p3);
            for (var i = 0; i < 4; i++)
                backingNormals.Add(normal);
            backingUvs.AddRange(uv);
            backingIndices.Add(first);
            backingIndices.Add(first + 1);
            backingIndices.Add(first + 2);
            backingIndices.Add(first);
            backingIndices.Add(first + 2);
            backingIndices.Add(first + 3);
        }

        private static int EncodeFace(int3 block, int face) =>
            ((block.x + ChunkCoordinates.Size *
              (block.y + ChunkCoordinates.Size * block.z)) * 6) + face;

        private static int ResolveFace(Vector3 normal)
        {
            if (normal.z < -0.5f) return 0;
            if (normal.z > 0.5f) return 1;
            if (normal.x > 0.5f) return 2;
            if (normal.x < -0.5f) return 3;
            return normal.y > 0.5f ? 4 : 5;
        }

        private static int3 FaceOffset(int face)
        {
            switch (face)
            {
                case 0: return new int3(0, 0, -1);
                case 1: return new int3(0, 0, 1);
                case 2: return new int3(1, 0, 0);
                case 3: return new int3(-1, 0, 0);
                case 4: return new int3(0, 1, 0);
                default: return new int3(0, -1, 0);
            }
        }

        private static Vector3 FaceNormal(int face)
        {
            var offset = FaceOffset(face);
            return new Vector3(offset.x, offset.y, offset.z);
        }

        private static void GetFaceCorners(
            int face,
            out Vector3 p0,
            out Vector3 p1,
            out Vector3 p2,
            out Vector3 p3)
        {
            const float half = 0.5f;
            switch (face)
            {
                case 0:
                    p0 = new Vector3(-half, -half, -half); p1 = new Vector3(-half, half, -half);
                    p2 = new Vector3(half, half, -half); p3 = new Vector3(half, -half, -half); break;
                case 1:
                    p0 = new Vector3(half, -half, half); p1 = new Vector3(half, half, half);
                    p2 = new Vector3(-half, half, half); p3 = new Vector3(-half, -half, half); break;
                case 2:
                    p0 = new Vector3(half, -half, -half); p1 = new Vector3(half, half, -half);
                    p2 = new Vector3(half, half, half); p3 = new Vector3(half, -half, half); break;
                case 3:
                    p0 = new Vector3(-half, -half, half); p1 = new Vector3(-half, half, half);
                    p2 = new Vector3(-half, half, -half); p3 = new Vector3(-half, -half, -half); break;
                case 4:
                    p0 = new Vector3(-half, half, -half); p1 = new Vector3(-half, half, half);
                    p2 = new Vector3(half, half, half); p3 = new Vector3(half, half, -half); break;
                default:
                    p0 = new Vector3(-half, -half, half); p1 = new Vector3(-half, -half, -half);
                    p2 = new Vector3(half, -half, -half); p3 = new Vector3(half, -half, half); break;
            }
        }

        private int3 ResolveTriangleBlock(int index0, int index1, int index2)
        {
            var faceCenter =
                (vertexPositions[index0] + vertexPositions[index1] + vertexPositions[index2]) /
                3f;
            var normal = vertexNormals[index0];
            var blockCenter = faceCenter - normal * 0.5f;
            return new int3(
                Mathf.RoundToInt(blockCenter.x),
                Mathf.RoundToInt(blockCenter.y),
                Mathf.RoundToInt(blockCenter.z));
        }

        private void ClearCachedGeometry()
        {
            for (var index = 0; index < baseIndices.Length; index++)
            {
                baseIndices[index] = null;
                routedIndices[index].Clear();
            }
            occlusionIndices.Clear();
            vertexPositions.Clear();
            vertexNormals.Clear();
            vertexUvs.Clear();
            visibleBlockFaces.Clear();
            blockFaceUvs.Clear();
            backingVertices.Clear();
            backingNormals.Clear();
            backingUvs.Clear();
            backingIndices.Clear();
            ditheredBlocks.Clear();
            hasBaseGeometry = false;
        }

        private static void DestroyOwnedMesh(Mesh owned)
        {
            if (owned == null)
                return;
            if (Application.isPlaying)
                Destroy(owned);
            else
                DestroyImmediate(owned);
        }
    }
}
