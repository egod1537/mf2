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

        public void Configure(int3 chunkCoordinate, Material[] sharedMaterials)
        {
            EnsureInitialized();
            ChunkCoordinate = chunkCoordinate;
            transform.localPosition = new Vector3(
                chunkCoordinate.x * (float)ChunkCoordinates.Size,
                chunkCoordinate.y * (float)ChunkCoordinates.Size,
                chunkCoordinate.z * (float)ChunkCoordinates.Size);
            meshRenderer.sharedMaterials = sharedMaterials;
            gameObject.name = $"Chunk ({chunkCoordinate.x}, {chunkCoordinate.y}, {chunkCoordinate.z})";
        }

        public void SetRenderingEnabled(bool enabled)
        {
            EnsureInitialized();
            meshRenderer.enabled = enabled;
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
            mesh = null;
            colliderMesh = null;
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
        }

        private void CaptureBaseGeometry()
        {
            vertexPositions.Clear();
            vertexNormals.Clear();
            mesh.GetVertices(vertexPositions);
            mesh.GetNormals(vertexNormals);
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
