using System;
using System.Collections.Generic;
using System.IO;
using MF2.World.Registry;
using MF2.World.Rendering;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MF2.World.Editor.Registry
{
    /// <summary>
    /// Exports exact UV corners from the already-generated Minecraft cube meshes. The source
    /// atlas, material, meshes, and prefabs are read-only inputs; only Assets/World is written.
    /// </summary>
    public static class BlockRenderManifestBuilder
    {
        public const string RegistryManifestPath =
            "Assets/World/Registry/BlockRegistryManifest.asset";
        public const string AtlasPath =
            "Assets/Minecraft/Atlas/minecraft_blocks_atlas.png";
        public const string SourceMaterialPath =
            "Assets/Minecraft/Materials/MinecraftBlockAtlas.mat";
        public const string RenderFolder = "Assets/World/Rendering";
        public const string RenderManifestPath = RenderFolder + "/BlockRenderManifest.asset";
        public const string MaterialSetPath = RenderFolder + "/ChunkMaterialSet.asset";
        public const string OpaqueMaterialPath = RenderFolder + "/MinecraftChunkOpaque.mat";
        public const string CutoutMaterialPath = RenderFolder + "/MinecraftChunkCutout.mat";
        public const string TransparentMaterialPath = RenderFolder + "/MinecraftChunkTransparent.mat";

        [MenuItem("Tools/Minecraft/Rebuild Block Render Manifest")]
        public static BlockRenderManifest RebuildFromRegistryManifest()
        {
            var registryManifest =
                AssetDatabase.LoadAssetAtPath<BlockRegistryManifest>(RegistryManifestPath);
            if (registryManifest == null)
                throw new InvalidOperationException("Build the block registry manifest first.");

            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPath);
            if (atlas == null || sourceMaterial == null)
                throw new InvalidOperationException("The generated Minecraft atlas assets are missing.");

            var decodedAtlas = DecodeAtlas();
            try
            {
                var pixels = decodedAtlas.GetPixels32();
                var entries = new BlockRenderManifest.Entry[registryManifest.Entries.Count];
                for (var i = 0; i < registryManifest.Entries.Count; i++)
                {
                    var registryEntry = registryManifest.Entries[i];
                    var meshFilter = registryEntry.Prefab != null
                        ? registryEntry.Prefab.GetComponentInChildren<MeshFilter>(true)
                        : null;
                    var mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                    ValidateCubeMesh(registryEntry.CanonicalName, mesh);

                    var uvs = mesh.uv;
                    var faces = new BlockRenderManifest.Face[6];
                    for (var faceIndex = 0; faceIndex < faces.Length; faceIndex++)
                    {
                        var offset = faceIndex * 4;
                        var pass = ClassifyPass(
                            uvs,
                            offset,
                            pixels,
                            decodedAtlas.width,
                            decodedAtlas.height);
                        faces[faceIndex] = new BlockRenderManifest.Face(
                            uvs[offset],
                            uvs[offset + 1],
                            uvs[offset + 2],
                            uvs[offset + 3],
                            pass);
                    }

                    entries[i] = new BlockRenderManifest.Entry(
                        registryEntry.CanonicalName,
                        StableBlockId.Compute(registryEntry.CanonicalName),
                        BlockGeometryKind.Cube,
                        faces);
                }

                // Validate duplicate/reserved IDs and native conversion before writing assets.
                var definitions = new List<BlockRenderDefinition>(entries.Length);
                for (var i = 0; i < entries.Length; i++)
                    definitions.Add(entries[i].ToRuntime());
                using (var validation = new BlockRenderTable(definitions, Allocator.Temp))
                {
                    if (validation.Count != entries.Length)
                        throw new InvalidOperationException("Render table validation was incomplete.");
                }

                EnsureFolder(RenderFolder);
                var opaque = CreateOrUpdateMaterial(
                    OpaqueMaterialPath,
                    "MinecraftChunkOpaque",
                    sourceMaterial,
                    atlas,
                    BlockRenderPass.Opaque);
                var cutout = CreateOrUpdateMaterial(
                    CutoutMaterialPath,
                    "MinecraftChunkCutout",
                    sourceMaterial,
                    atlas,
                    BlockRenderPass.Cutout);
                var transparent = CreateOrUpdateMaterial(
                    TransparentMaterialPath,
                    "MinecraftChunkTransparent",
                    sourceMaterial,
                    atlas,
                    BlockRenderPass.Transparent);

                var manifest = AssetDatabase.LoadAssetAtPath<BlockRenderManifest>(RenderManifestPath);
                if (manifest == null)
                {
                    manifest = ScriptableObject.CreateInstance<BlockRenderManifest>();
                    AssetDatabase.CreateAsset(manifest, RenderManifestPath);
                }

                manifest.ReplaceEntries(
                    AssetDatabase.AssetPathToGUID(AtlasPath),
                    atlas,
                    sourceMaterial,
                    entries);
                EditorUtility.SetDirty(manifest);

                var materialSet = AssetDatabase.LoadAssetAtPath<ChunkMaterialSet>(MaterialSetPath);
                if (materialSet == null)
                {
                    materialSet = ScriptableObject.CreateInstance<ChunkMaterialSet>();
                    AssetDatabase.CreateAsset(materialSet, MaterialSetPath);
                }

                materialSet.Configure(atlas, opaque, cutout, transparent);
                EditorUtility.SetDirty(materialSet);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    $"Block render manifest exported {entries.Length} cube definitions " +
                    $"from existing meshes without modifying Minecraft source assets.",
                    manifest);
                return manifest;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(decodedAtlas);
            }
        }

        private static Texture2D DecodeAtlas()
        {
            var absolutePath = Path.Combine(Directory.GetCurrentDirectory(), AtlasPath);
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!ImageConversion.LoadImage(decoded, File.ReadAllBytes(absolutePath), false))
            {
                UnityEngine.Object.DestroyImmediate(decoded);
                throw new InvalidDataException("Failed to decode the Minecraft atlas PNG.");
            }

            return decoded;
        }

        private static void ValidateCubeMesh(string canonicalName, Mesh mesh)
        {
            if (mesh == null)
                throw new InvalidOperationException($"'{canonicalName}' has no generated mesh.");
            if (mesh.vertexCount != 24 || mesh.uv == null || mesh.uv.Length != 24)
            {
                throw new InvalidOperationException(
                    $"'{canonicalName}' is not the expected 24-vertex generated cube mesh.");
            }
        }

        private static BlockRenderPass ClassifyPass(
            Vector2[] uvs,
            int offset,
            Color32[] pixels,
            int width,
            int height)
        {
            var minU = 1f;
            var maxU = 0f;
            var minV = 1f;
            var maxV = 0f;
            for (var i = 0; i < 4; i++)
            {
                var uv = uvs[offset + i];
                minU = Mathf.Min(minU, uv.x);
                maxU = Mathf.Max(maxU, uv.x);
                minV = Mathf.Min(minV, uv.y);
                maxV = Mathf.Max(maxV, uv.y);
            }

            var xMin = Mathf.Clamp(Mathf.FloorToInt(minU * width), 0, width - 1);
            var xMax = Mathf.Clamp(Mathf.FloorToInt(maxU * width), 0, width - 1);
            var yMin = Mathf.Clamp(Mathf.FloorToInt(minV * height), 0, height - 1);
            var yMax = Mathf.Clamp(Mathf.FloorToInt(maxV * height), 0, height - 1);
            var hasTransparent = false;
            var hasPartial = false;
            for (var y = yMin; y <= yMax; y++)
            {
                for (var x = xMin; x <= xMax; x++)
                {
                    var alpha = pixels[y * width + x].a;
                    hasTransparent |= alpha < byte.MaxValue;
                    hasPartial |= alpha > 0 && alpha < byte.MaxValue;
                }
            }

            if (hasPartial)
                return BlockRenderPass.Transparent;
            return hasTransparent ? BlockRenderPass.Cutout : BlockRenderPass.Opaque;
        }

        private static Material CreateOrUpdateMaterial(
            string path,
            string materialName,
            Material source,
            Texture2D atlas,
            BlockRenderPass pass)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            material.name = materialName;
            SetAtlas(material, atlas);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            switch (pass)
            {
                case BlockRenderPass.Cutout:
                    SetFloat(material, "_Surface", 0f);
                    SetFloat(material, "_AlphaClip", 1f);
                    SetFloat(material, "_Cutoff", 0.1f);
                    SetFloat(material, "_SrcBlend", (float)BlendMode.One);
                    SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
                    SetFloat(material, "_SrcBlendAlpha", (float)BlendMode.One);
                    SetFloat(material, "_DstBlendAlpha", (float)BlendMode.Zero);
                    SetFloat(material, "_ZWrite", 1f);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                    break;
                case BlockRenderPass.Transparent:
                    SetFloat(material, "_Surface", 1f);
                    SetFloat(material, "_AlphaClip", 0f);
                    SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                    SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    SetFloat(material, "_SrcBlendAlpha", (float)BlendMode.One);
                    SetFloat(material, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    SetFloat(material, "_ZWrite", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)RenderQueue.Transparent;
                    break;
                default:
                    SetFloat(material, "_Surface", 0f);
                    SetFloat(material, "_AlphaClip", 0f);
                    SetFloat(material, "_SrcBlend", (float)BlendMode.One);
                    SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
                    SetFloat(material, "_SrcBlendAlpha", (float)BlendMode.One);
                    SetFloat(material, "_DstBlendAlpha", (float)BlendMode.Zero);
                    SetFloat(material, "_ZWrite", 1f);
                    material.SetOverrideTag("RenderType", "Opaque");
                    material.renderQueue = (int)RenderQueue.Geometry;
                    break;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetAtlas(Material material, Texture2D atlas)
        {
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", atlas);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", atlas);
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static void EnsureFolder(string assetPath)
        {
            var parts = assetPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
