using System;
using System.Collections.Generic;
using MF2.World.Registry;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MF2.World.Rendering
{
    /// <summary>
    /// Authoring export of the existing Minecraft cube meshes. It records the emitted corner UVs
    /// verbatim, rather than assuming atlas cell order or reconstructing rects at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "BlockRenderManifest", menuName = "MF2/World/Block Render Manifest")]
    public sealed class BlockRenderManifest : ScriptableObject
    {
        [Serializable]
        public sealed class Face
        {
            [SerializeField] private Vector2 uv0;
            [SerializeField] private Vector2 uv1;
            [SerializeField] private Vector2 uv2;
            [SerializeField] private Vector2 uv3;
            [SerializeField] private BlockRenderPass renderPass;

            public Vector2 Uv0 => uv0;
            public Vector2 Uv1 => uv1;
            public Vector2 Uv2 => uv2;
            public Vector2 Uv3 => uv3;
            public BlockRenderPass RenderPass => renderPass;

            internal Face(Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3, BlockRenderPass pass)
            {
                this.uv0 = uv0;
                this.uv1 = uv1;
                this.uv2 = uv2;
                this.uv3 = uv3;
                renderPass = pass;
            }

            internal BlockFaceRenderDefinition ToRuntime()
            {
                return new BlockFaceRenderDefinition(uv0, uv1, uv2, uv3, renderPass);
            }
        }

        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string canonicalName;
            [SerializeField] private uint stableId;
            [SerializeField] private BlockGeometryKind geometryKind;
            [SerializeField] private Face[] faces = Array.Empty<Face>();

            public string CanonicalName => canonicalName;
            public uint StableId => stableId;
            public BlockGeometryKind GeometryKind => geometryKind;
            public IReadOnlyList<Face> Faces => faces;

            internal Entry(
                string canonicalName,
                uint stableId,
                BlockGeometryKind geometryKind,
                Face[] faces)
            {
                this.canonicalName = canonicalName;
                this.stableId = stableId;
                this.geometryKind = geometryKind;
                this.faces = faces;
            }

            internal BlockRenderDefinition ToRuntime()
            {
                if (faces == null || faces.Length != 6)
                    throw new InvalidOperationException($"'{canonicalName}' must define exactly six faces.");

                return new BlockRenderDefinition
                {
                    Id = stableId,
                    GeometryKind = geometryKind,
                    North = faces[(int)BlockFace.North].ToRuntime(),
                    South = faces[(int)BlockFace.South].ToRuntime(),
                    East = faces[(int)BlockFace.East].ToRuntime(),
                    West = faces[(int)BlockFace.West].ToRuntime(),
                    Up = faces[(int)BlockFace.Up].ToRuntime(),
                    Down = faces[(int)BlockFace.Down].ToRuntime()
                };
            }
        }

        [SerializeField] private int idSchemeVersion = StableBlockId.SchemeVersion;
        [SerializeField] private string sourceAtlasGuid;
        [SerializeField] private Texture2D atlas;
        [SerializeField] private Material sourceAtlasMaterial;
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public int IdSchemeVersion => idSchemeVersion;
        public string SourceAtlasGuid => sourceAtlasGuid;
        public Texture2D Atlas => atlas;
        public Material SourceAtlasMaterial => sourceAtlasMaterial;
        public IReadOnlyList<Entry> Entries => entries;

        public BlockRenderTable CreateRuntimeTable(Allocator allocator = Allocator.Persistent)
        {
            if (idSchemeVersion != StableBlockId.SchemeVersion)
            {
                throw new InvalidOperationException(
                    $"Render manifest ID scheme {idSchemeVersion} does not match runtime scheme " +
                    $"{StableBlockId.SchemeVersion}.");
            }

            var runtime = new BlockRenderDefinition[entries.Length];
            for (var i = 0; i < entries.Length; i++)
                runtime[i] = entries[i].ToRuntime();
            return new BlockRenderTable(runtime, allocator);
        }

#if UNITY_EDITOR
        internal void ReplaceEntries(
            string atlasGuid,
            Texture2D sourceAtlas,
            Material sourceMaterial,
            Entry[] replacement)
        {
            idSchemeVersion = StableBlockId.SchemeVersion;
            sourceAtlasGuid = atlasGuid;
            atlas = sourceAtlas;
            sourceAtlasMaterial = sourceMaterial;
            entries = replacement ?? Array.Empty<Entry>();
        }
#endif
    }
}
