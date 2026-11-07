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
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;
        private Mesh mesh;

        public int3 ChunkCoordinate { get; private set; }
        public ChunkHandle AppliedHandle { get; private set; }
        public ulong AppliedVersion { get; private set; }
        public bool RenderingEnabled => meshRenderer != null && meshRenderer.enabled;

        public Mesh Mesh
        {
            get
            {
                EnsureInitialized();
                return mesh;
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
            if (!request.TryApply(world, mesh))
                return false;

            // Reassigning through null makes PhysX recook the exact accepted mesh version.
            meshCollider.sharedMesh = null;
            if (mesh.vertexCount > 0)
                meshCollider.sharedMesh = mesh;

            AppliedHandle = request.Handle;
            AppliedVersion = request.Version;
            return true;
        }

        public void Clear()
        {
            if (meshCollider != null)
                meshCollider.sharedMesh = null;
            if (mesh != null)
                mesh.Clear(false);
            AppliedHandle = default;
            AppliedVersion = 0;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (mesh == null)
                return;

            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);
            mesh = null;
        }

        private void EnsureInitialized()
        {
            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null)
                meshRenderer = GetComponent<MeshRenderer>();
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
        }
    }
}
