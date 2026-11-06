#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MF2.Minecraft.Editor
{
    /// <summary>
    /// Converts the installed Minecraft Java block model definitions into Unity cube prefabs.
    /// Only true 16x16x16 cube models are emitted; plants, doors, rails, slabs and other custom
    /// geometry are intentionally skipped.
    /// </summary>
    public static class MinecraftBlockPrefabGenerator
    {
        public const string SourceVersion = "26.3";

        private const string RootFolder = "Assets/Minecraft";
        private const string TextureFolder = RootFolder + "/Textures/Block";
        private const string ModelFolder = RootFolder + "/Models";
        private const string BlockMeshFolder = ModelFolder + "/Blocks";
        private const string MaterialFolder = RootFolder + "/Materials";
        private const string AtlasFolder = RootFolder + "/Atlas";
        private const string PrefabFolder = RootFolder + "/Prefabs/Blocks";
        private const string CubeMeshPath = ModelFolder + "/MinecraftCube.asset";
        private const string AtlasTexturePath = AtlasFolder + "/minecraft_blocks_atlas.png";
        private const string AtlasMaterialPath = MaterialFolder + "/MinecraftBlockAtlas.mat";

        private const int AtlasTileSize = 16;
        private const int AtlasPadding = 2;
        private const int AtlasCellSize = AtlasTileSize + AtlasPadding * 2;

        private static readonly Color BiomeTint = new Color32(145, 189, 89, 255);

        private const string BlockModelPrefix = "assets/minecraft/models/block/";
        private const string BlockstatePrefix = "assets/minecraft/blockstates/";

        private static readonly string[] FaceNames =
        {
            "north", "south", "east", "west", "up", "down"
        };

        [Serializable]
        public sealed class GenerationResult
        {
            public string sourceVersion;
            public string sourceJar;
            public string outputFolder;
            public int blockstatesRead;
            public int modelsRead;
            public int cubeBlocksFound;
            public int skippedNonCube;
            public int skippedMissingTextures;
            public int materialsUsed;
            public int atlasEntries;
            public int atlasSize;
            public int blockMeshes;
            public int legacyMaterialsDeleted;
            public int prefabsCreated;
            public int prefabsUpdated;
            public string[] sampleBlocks;
        }

        private sealed class ResolvedModel
        {
            public readonly Dictionary<string, TextureReference> Textures =
                new Dictionary<string, TextureReference>(StringComparer.Ordinal);

            public JArray Elements;
        }

        private sealed class TextureReference
        {
            public string Sprite;
            public bool ForceTranslucent;
        }

        private sealed class BlockDefinition
        {
            public string Name;
            public readonly string[] FaceTextureIds = new string[6];
            public readonly bool[] FaceForceTranslucent = new bool[6];
            public readonly bool[] FaceTinted = new bool[6];
        }

        private sealed class SourceData
        {
            public string JarPath;
            public int BlockstateCount;
            public int ModelCount;
            public int SkippedNonCube;
            public int SkippedMissingTextures;
            public List<BlockDefinition> Definitions;
        }

        private sealed class AtlasSource
        {
            public string Key;
            public string TextureId;
            public bool ForceTranslucent;
            public bool Tinted;
        }

        private sealed class AtlasBuild
        {
            public Texture2D Texture;
            public readonly Dictionary<string, Rect> Rects =
                new Dictionary<string, Rect>(StringComparer.Ordinal);
            public int Size;
            public int EntryCount;
        }

        [MenuItem("Tools/Minecraft/Generate Cube Block Prefabs")]
        public static void GenerateFromMenu()
        {
            var result = GenerateAll();
            Debug.Log(
                $"Minecraft block generation complete: {result.cubeBlocksFound} cube prefabs, " +
                $"{result.materialsUsed} materials, source {result.sourceVersion}.");
        }

        [MenuItem("Tools/Minecraft/Analyze Cube Block Prefabs")]
        public static void AnalyzeFromMenu()
        {
            var result = Analyze();
            Debug.Log(
                $"Minecraft source {result.sourceVersion}: {result.cubeBlocksFound} full cubes, " +
                $"{result.skippedNonCube} non-cube blockstates skipped, " +
                $"{result.skippedMissingTextures} missing-texture blockstates skipped.");
        }

        public static GenerationResult Analyze()
        {
            var source = ReadSource();
            return ToResult(source);
        }

        public static GenerationResult GenerateAll()
        {
            var source = ReadSource();

            EnsureFolder(ModelFolder);
            EnsureFolder(BlockMeshFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(AtlasFolder);
            EnsureFolder(PrefabFolder);

            var atlas = CreateAtlas(source.Definitions);
            var atlasMaterial = CreateOrUpdateAtlasMaterial(atlas.Texture);
            var generatedMeshes = new List<Mesh>(source.Definitions.Count);
            var prefabsCreated = 0;
            var prefabsUpdated = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var definition in source.Definitions)
                {
                    var blockMesh = CreateOrUpdateBlockMesh(definition, atlas.Rects);
                    generatedMeshes.Add(blockMesh);

                    var prefabPath = PrefabFolder + "/" +
                                     SanitizeFileName(definition.Name) + ".prefab";
                    var existed = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;

                    var block = new GameObject(definition.Name);
                    try
                    {
                        var filter = block.AddComponent<MeshFilter>();
                        filter.sharedMesh = blockMesh;

                        var renderer = block.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = atlasMaterial;
                        renderer.shadowCastingMode = ShadowCastingMode.On;
                        renderer.receiveShadows = true;

                        var collider = block.AddComponent<BoxCollider>();
                        collider.center = Vector3.zero;
                        collider.size = Vector3.one;

                        bool saveSucceeded;
                        PrefabUtility.SaveAsPrefabAsset(block, prefabPath, out saveSucceeded);
                        if (!saveSucceeded)
                        {
                            throw new InvalidOperationException(
                                "Failed to save Minecraft block prefab: " + prefabPath);
                        }
                    }
                    finally
                    {
                        Object.DestroyImmediate(block);
                    }

                    if (existed)
                        prefabsUpdated++;
                    else
                        prefabsCreated++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            foreach (var mesh in generatedMeshes)
                AssetDatabase.SaveAssetIfDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(atlasMaterial);

            // The atlas migration supersedes the per-texture materials and the old six-submesh
            // cube. Delete only assets produced by earlier versions of this generator.
            var legacyMaterialsDeleted = DeleteLegacyGeneratedMaterials();
            if (AssetDatabase.LoadAssetAtPath<Mesh>(CubeMeshPath) != null)
                AssetDatabase.DeleteAsset(CubeMeshPath);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var result = ToResult(source);
            result.materialsUsed = 1;
            result.atlasEntries = atlas.EntryCount;
            result.atlasSize = atlas.Size;
            result.blockMeshes = generatedMeshes.Count;
            result.legacyMaterialsDeleted = legacyMaterialsDeleted;
            result.prefabsCreated = prefabsCreated;
            result.prefabsUpdated = prefabsUpdated;
            return result;
        }

        private static SourceData ReadSource()
        {
            var jarPath = FindSourceJar();
            var models = new Dictionary<string, JObject>(StringComparer.Ordinal);
            var blockstates = new List<KeyValuePair<string, JObject>>();

            using (var archive = ZipFile.OpenRead(jarPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.StartsWith(BlockModelPrefix, StringComparison.Ordinal) &&
                        entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        var relativeName = entry.FullName.Substring(BlockModelPrefix.Length);
                        relativeName = relativeName.Substring(0, relativeName.Length - 5);
                        models["minecraft:block/" + relativeName] = ReadJson(entry);
                    }
                    else if (entry.FullName.StartsWith(BlockstatePrefix, StringComparison.Ordinal) &&
                             entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        var relativeName = entry.FullName.Substring(BlockstatePrefix.Length);
                        relativeName = relativeName.Substring(0, relativeName.Length - 5);
                        blockstates.Add(new KeyValuePair<string, JObject>(
                            relativeName, ReadJson(entry)));
                    }
                }
            }

            var resolvedCache = new Dictionary<string, ResolvedModel>(StringComparer.Ordinal);
            var definitions = new List<BlockDefinition>();
            var skippedNonCube = 0;
            var skippedMissingTextures = 0;

            foreach (var blockstate in blockstates.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var modelIds = blockstate.Value.SelectTokens("$..model")
                    .Where(token => token.Type == JTokenType.String)
                    .Select(token => NormalizeModelId(token.Value<string>()))
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var exactModelId = "minecraft:block/" + blockstate.Key;
                modelIds = modelIds
                    .OrderBy(id => string.Equals(id, exactModelId, StringComparison.Ordinal) ? 0 : 1)
                    .ThenBy(id => id, StringComparer.Ordinal)
                    .ToList();

                var resolvedModels = modelIds
                    .Select(modelId => ResolveModel(
                        modelId, models, resolvedCache,
                        new HashSet<string>(StringComparer.Ordinal)))
                    .Where(model => model != null)
                    .ToList();

                // A slab, snow layer, wall, etc. can reference one full-cube variant while
                // still being a non-cube block in its other states. Only emit a block when
                // every model used by its blockstate is geometrically a full cube.
                if (resolvedModels.Count == 0 || resolvedModels.Any(model => !IsFullCubeGeometry(model)))
                {
                    skippedNonCube++;
                    continue;
                }

                BlockDefinition definition = null;
                var foundCubeWithMissingTexture = false;
                foreach (var modelId in modelIds)
                {
                    var model = ResolveModel(
                        modelId, models, resolvedCache,
                        new HashSet<string>(StringComparer.Ordinal));
                    if (model == null)
                        continue;

                    var candidate = new BlockDefinition { Name = blockstate.Key };
                    if (!TryReadCubeTextures(
                            model,
                            candidate.FaceTextureIds,
                            candidate.FaceForceTranslucent,
                            candidate.FaceTinted))
                        continue;

                    if (!candidate.FaceTextureIds.All(TextureAssetExists))
                    {
                        foundCubeWithMissingTexture = true;
                        continue;
                    }

                    definition = candidate;
                    break;
                }

                if (definition != null)
                    definitions.Add(definition);
                else if (foundCubeWithMissingTexture)
                    skippedMissingTextures++;
                else
                    skippedNonCube++;
            }

            return new SourceData
            {
                JarPath = jarPath,
                BlockstateCount = blockstates.Count,
                ModelCount = models.Count,
                SkippedNonCube = skippedNonCube,
                SkippedMissingTextures = skippedMissingTextures,
                Definitions = definitions
            };
        }

        private static JObject ReadJson(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream))
            {
                return JObject.Parse(reader.ReadToEnd());
            }
        }

        private static string FindSourceJar()
        {
            var minecraftFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft", "versions");
            var preferred = Path.Combine(
                minecraftFolder, SourceVersion, SourceVersion + ".jar");
            if (File.Exists(preferred))
                return preferred;

            throw new FileNotFoundException(
                "Minecraft Java Edition source JAR was not found.", preferred);
        }

        private static ResolvedModel ResolveModel(
            string modelId,
            IDictionary<string, JObject> models,
            IDictionary<string, ResolvedModel> cache,
            ISet<string> resolving)
        {
            ResolvedModel cached;
            if (cache.TryGetValue(modelId, out cached))
                return cached;

            JObject json;
            if (!models.TryGetValue(modelId, out json))
                return null;

            if (!resolving.Add(modelId))
                throw new InvalidDataException("Circular Minecraft model parent chain: " + modelId);

            var result = new ResolvedModel();
            var parentId = NormalizeModelId(json.Value<string>("parent"));
            if (!string.IsNullOrEmpty(parentId))
            {
                var parent = ResolveModel(parentId, models, cache, resolving);
                if (parent != null)
                {
                    foreach (var texture in parent.Textures)
                        result.Textures[texture.Key] = texture.Value;
                    result.Elements = parent.Elements;
                }
            }

            var textures = json["textures"] as JObject;
            if (textures != null)
            {
                foreach (var texture in textures.Properties())
                {
                    var reference = ParseTextureReference(texture.Value);
                    if (reference != null)
                        result.Textures[texture.Name] = reference;
                }
            }

            var elements = json["elements"] as JArray;
            if (elements != null)
                result.Elements = elements;

            resolving.Remove(modelId);
            cache[modelId] = result;
            return result;
        }

        private static TextureReference ParseTextureReference(JToken token)
        {
            if (token == null)
                return null;

            if (token.Type == JTokenType.String)
            {
                return new TextureReference
                {
                    Sprite = token.Value<string>(),
                    ForceTranslucent = false
                };
            }

            var json = token as JObject;
            var sprite = json == null ? null : json.Value<string>("sprite");
            if (string.IsNullOrEmpty(sprite))
                return null;

            return new TextureReference
            {
                Sprite = sprite,
                ForceTranslucent = json.Value<bool?>("force_translucent") ?? false
            };
        }

        private static bool TryReadCubeTextures(
            ResolvedModel model,
            string[] destination,
            bool[] forceTranslucent,
            bool[] tinted)
        {
            if (!IsFullCubeGeometry(model))
                return false;

            var element = model.Elements
                .OfType<JObject>()
                .First(item =>
                {
                    var itemFaces = item["faces"] as JObject;
                    return itemFaces != null && FaceNames.All(face => itemFaces[face] is JObject);
                });
            var faces = element["faces"] as JObject;

            for (var i = 0; i < FaceNames.Length; i++)
            {
                var face = faces[FaceNames[i]] as JObject;
                if (face == null)
                    return false;

                var rotation = face.Value<int?>("rotation") ?? 0;
                if (rotation % 360 != 0)
                    return false;

                var uv = face["uv"] as JArray;
                if (uv != null && !UvEqualsFullFace(uv))
                    return false;

                var textureToken = face.Value<string>("texture");
                var texture = ResolveTextureReference(textureToken, model.Textures);
                if (texture == null || string.IsNullOrEmpty(texture.Sprite) ||
                    !texture.Sprite.StartsWith("minecraft:block/", StringComparison.Ordinal))
                    return false;

                destination[i] = texture.Sprite;
                forceTranslucent[i] = texture.ForceTranslucent;
                tinted[i] = face["tintindex"] != null;
            }

            return true;
        }

        private static bool IsFullCubeGeometry(ResolvedModel model)
        {
            if (model == null || model.Elements == null || model.Elements.Count == 0)
                return false;

            var hasCompleteBase = false;
            foreach (var element in model.Elements.OfType<JObject>())
            {
                if (element["rotation"] != null ||
                    !VectorEquals(element["from"] as JArray, 0f, 0f, 0f) ||
                    !VectorEquals(element["to"] as JArray, 16f, 16f, 16f))
                    return false;

                var faces = element["faces"] as JObject;
                if (faces == null)
                    return false;
                if (FaceNames.All(face => faces[face] is JObject))
                    hasCompleteBase = true;
            }

            return hasCompleteBase;
        }

        private static TextureReference ResolveTextureReference(
            string token, IDictionary<string, TextureReference> textures)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var forceTranslucent = false;
            while (!string.IsNullOrEmpty(token) && token[0] == '#')
            {
                var key = token.Substring(1);
                TextureReference reference;
                if (!visited.Add(key) || !textures.TryGetValue(key, out reference))
                    return null;
                token = reference.Sprite;
                forceTranslucent |= reference.ForceTranslucent;
            }

            if (string.IsNullOrEmpty(token))
                return null;
            if (token.IndexOf(':') < 0)
                token = "minecraft:" + token;
            return new TextureReference
            {
                Sprite = token,
                ForceTranslucent = forceTranslucent
            };
        }

        private static string NormalizeModelId(string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
                return null;
            return modelId.IndexOf(':') >= 0 ? modelId : "minecraft:" + modelId;
        }

        private static bool TextureAssetExists(string textureId)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TextureAssetPath(textureId)) != null;
        }

        private static string TextureAssetPath(string textureId)
        {
            const string prefix = "minecraft:block/";
            if (!textureId.StartsWith(prefix, StringComparison.Ordinal))
                return null;
            return TextureFolder + "/" + textureId.Substring(prefix.Length) + ".png";
        }

        private static AtlasBuild CreateAtlas(IList<BlockDefinition> definitions)
        {
            var sources = new Dictionary<string, AtlasSource>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                for (var face = 0; face < FaceNames.Length; face++)
                {
                    var key = AtlasKey(
                        definition.FaceTextureIds[face],
                        definition.FaceForceTranslucent[face],
                        definition.FaceTinted[face]);
                    if (!sources.ContainsKey(key))
                    {
                        sources[key] = new AtlasSource
                        {
                            Key = key,
                            TextureId = definition.FaceTextureIds[face],
                            ForceTranslucent = definition.FaceForceTranslucent[face],
                            Tinted = definition.FaceTinted[face]
                        };
                    }
                }
            }

            var orderedSources = sources.Values.OrderBy(item => item.Key, StringComparer.Ordinal).ToList();
            var minimumCellsPerAxis = Mathf.CeilToInt(Mathf.Sqrt(orderedSources.Count));
            var atlasSize = Mathf.NextPowerOfTwo(minimumCellsPerAxis * AtlasCellSize);
            atlasSize = Mathf.Max(128, atlasSize);
            var cellsPerAxis = atlasSize / AtlasCellSize;
            if (cellsPerAxis * cellsPerAxis < orderedSources.Count)
                throw new InvalidOperationException("The generated Minecraft atlas is too small.");

            var atlasPixels = new Color32[atlasSize * atlasSize];
            var build = new AtlasBuild
            {
                Size = atlasSize,
                EntryCount = orderedSources.Count
            };

            for (var index = 0; index < orderedSources.Count; index++)
            {
                var source = orderedSources[index];
                var tilePixels = ReadAtlasTile(source);
                var cellX = index % cellsPerAxis;
                var cellY = index / cellsPerAxis;
                var originX = cellX * AtlasCellSize;
                var originY = cellY * AtlasCellSize;

                for (var y = -AtlasPadding; y < AtlasTileSize + AtlasPadding; y++)
                {
                    var sourceY = Mathf.Clamp(y, 0, AtlasTileSize - 1);
                    for (var x = -AtlasPadding; x < AtlasTileSize + AtlasPadding; x++)
                    {
                        var sourceX = Mathf.Clamp(x, 0, AtlasTileSize - 1);
                        var targetX = originX + AtlasPadding + x;
                        var targetY = originY + AtlasPadding + y;
                        atlasPixels[targetY * atlasSize + targetX] =
                            tilePixels[sourceY * AtlasTileSize + sourceX];
                    }
                }

                var halfTexel = 0.5f / atlasSize;
                var uMin = (originX + AtlasPadding) / (float)atlasSize + halfTexel;
                var vMin = (originY + AtlasPadding) / (float)atlasSize + halfTexel;
                var uMax = (originX + AtlasPadding + AtlasTileSize) / (float)atlasSize - halfTexel;
                var vMax = (originY + AtlasPadding + AtlasTileSize) / (float)atlasSize - halfTexel;
                build.Rects[source.Key] = Rect.MinMaxRect(uMin, vMin, uMax, vMax);
            }

            var generatedTexture = new Texture2D(
                atlasSize, atlasSize, TextureFormat.RGBA32, false, false)
            {
                name = "minecraft_blocks_atlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            generatedTexture.SetPixels32(atlasPixels);
            generatedTexture.Apply(false, false);

            var png = generatedTexture.EncodeToPNG();
            Object.DestroyImmediate(generatedTexture);
            File.WriteAllBytes(ToAbsoluteProjectPath(AtlasTexturePath), png);

            AssetDatabase.ImportAsset(
                AtlasTexturePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(AtlasTexturePath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Failed to import the Minecraft texture atlas.");

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = Mathf.Max(2048, atlasSize);
            importer.isReadable = false;
            importer.SaveAndReimport();

            build.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasTexturePath);
            if (build.Texture == null)
                throw new InvalidOperationException("Minecraft texture atlas did not load after import.");
            return build;
        }

        private static Color32[] ReadAtlasTile(AtlasSource source)
        {
            var texturePath = TextureAssetPath(source.TextureId);
            var absolutePath = ToAbsoluteProjectPath(texturePath);
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!ImageConversion.LoadImage(decoded, File.ReadAllBytes(absolutePath), false))
                    throw new InvalidDataException("Failed to decode Minecraft texture: " + texturePath);

                var sourcePixels = decoded.GetPixels32();
                var frameSize = Mathf.Min(decoded.width, decoded.height);
                var frameY = decoded.height - frameSize;
                var result = new Color32[AtlasTileSize * AtlasTileSize];

                for (var y = 0; y < AtlasTileSize; y++)
                {
                    var sourceY = frameY + Mathf.Min(frameSize - 1, y * frameSize / AtlasTileSize);
                    for (var x = 0; x < AtlasTileSize; x++)
                    {
                        var sourceX = Mathf.Min(decoded.width - 1, x * decoded.width / AtlasTileSize);
                        var color = sourcePixels[sourceY * decoded.width + sourceX];
                        if (source.Tinted)
                        {
                            color.r = (byte)Mathf.RoundToInt(color.r * BiomeTint.r);
                            color.g = (byte)Mathf.RoundToInt(color.g * BiomeTint.g);
                            color.b = (byte)Mathf.RoundToInt(color.b * BiomeTint.b);
                        }
                        result[y * AtlasTileSize + x] = color;
                    }
                }

                return result;
            }
            finally
            {
                Object.DestroyImmediate(decoded);
            }
        }

        private static Material CreateOrUpdateAtlasMaterial(Texture2D atlasTexture)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AtlasMaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null)
                    throw new InvalidOperationException("No compatible Lit shader was found.");
                material = new Material(shader) { name = "MinecraftBlockAtlas" };
                AssetDatabase.CreateAsset(material, AtlasMaterialPath);
            }

            var textureProperty = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            material.SetTexture(textureProperty, atlasTexture);
            material.SetTextureScale(textureProperty, Vector2.one);
            material.SetTextureOffset(textureProperty, Vector2.zero);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
            SetFloatIfPresent(material, "_Metallic", 0f);
            SetFloatIfPresent(material, "_Smoothness", 0f);
            SetFloatIfPresent(material, "_SpecularHighlights", 0f);
            SetFloatIfPresent(material, "_Surface", 0f);
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.One);
            SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloatIfPresent(material, "_ZWrite", 1f);
            SetFloatIfPresent(material, "_AlphaClip", 1f);
            SetFloatIfPresent(material, "_Cutoff", 0.1f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh CreateOrUpdateBlockMesh(
            BlockDefinition definition,
            IDictionary<string, Rect> atlasRects)
        {
            var meshPath = BlockMeshFolder + "/" + SanitizeFileName(definition.Name) + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            var isNew = mesh == null;
            if (isNew)
                mesh = new Mesh { name = definition.Name };
            else
                mesh.Clear();

            const float half = 0.5f;
            mesh.vertices = new[]
            {
                new Vector3(-half, -half, -half), new Vector3(-half, half, -half),
                new Vector3(half, half, -half), new Vector3(half, -half, -half),
                new Vector3(half, -half, half), new Vector3(half, half, half),
                new Vector3(-half, half, half), new Vector3(-half, -half, half),
                new Vector3(half, -half, -half), new Vector3(half, half, -half),
                new Vector3(half, half, half), new Vector3(half, -half, half),
                new Vector3(-half, -half, half), new Vector3(-half, half, half),
                new Vector3(-half, half, -half), new Vector3(-half, -half, -half),
                new Vector3(-half, half, -half), new Vector3(-half, half, half),
                new Vector3(half, half, half), new Vector3(half, half, -half),
                new Vector3(-half, -half, half), new Vector3(-half, -half, -half),
                new Vector3(half, -half, -half), new Vector3(half, -half, half)
            };

            mesh.normals = FaceNames.SelectMany((face, index) =>
            {
                var normal = index == 0 ? Vector3.back :
                    index == 1 ? Vector3.forward :
                    index == 2 ? Vector3.right :
                    index == 3 ? Vector3.left :
                    index == 4 ? Vector3.up : Vector3.down;
                return new[] { normal, normal, normal, normal };
            }).ToArray();

            var uvs = new Vector2[FaceNames.Length * 4];
            for (var face = 0; face < FaceNames.Length; face++)
            {
                var key = AtlasKey(
                    definition.FaceTextureIds[face],
                    definition.FaceForceTranslucent[face],
                    definition.FaceTinted[face]);
                Rect rect;
                if (!atlasRects.TryGetValue(key, out rect))
                    throw new KeyNotFoundException("Missing Minecraft atlas entry: " + key);

                var offset = face * 4;
                uvs[offset] = new Vector2(rect.xMin, rect.yMin);
                uvs[offset + 1] = new Vector2(rect.xMin, rect.yMax);
                uvs[offset + 2] = new Vector2(rect.xMax, rect.yMax);
                uvs[offset + 3] = new Vector2(rect.xMax, rect.yMin);
            }
            mesh.uv = uvs;

            var triangles = new int[FaceNames.Length * 6];
            for (var face = 0; face < FaceNames.Length; face++)
            {
                var vertex = face * 4;
                var triangle = face * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }
            mesh.subMeshCount = 1;
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateBounds();

            if (isNew)
                AssetDatabase.CreateAsset(mesh, meshPath);
            else
                EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static int DeleteLegacyGeneratedMaterials()
        {
            var deleted = 0;
            var materialPaths = AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.Equals(path, AtlasMaterialPath, StringComparison.Ordinal))
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .StartsWith("minecraft_block_", StringComparison.Ordinal))
                .ToArray();

            foreach (var path in materialPaths)
            {
                if (AssetDatabase.DeleteAsset(path))
                    deleted++;
            }
            return deleted;
        }

        private static string AtlasKey(string textureId, bool forceTranslucent, bool tinted)
        {
            return textureId +
                   (forceTranslucent ? "|translucent" : "|standard") +
                   (tinted ? "|tinted" : "|untinted");
        }

        private static Material GetOrCreateMaterial(
            string textureId,
            bool forceTranslucent,
            bool tinted,
            IDictionary<string, Material> cache)
        {
            var cacheKey = textureId +
                           (forceTranslucent ? "|translucent" : "|standard") +
                           (tinted ? "|tinted" : "|untinted");
            Material material;
            if (cache.TryGetValue(cacheKey, out material))
                return material;

            var texturePath = TextureAssetPath(textureId);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
                throw new FileNotFoundException("Minecraft texture asset is missing.", texturePath);

            var materialName = SanitizeFileName(textureId.Replace(':', '_').Replace('/', '_'));
            if (forceTranslucent)
                materialName += "_translucent";
            if (tinted)
                materialName += "_tinted";
            var materialPath = MaterialFolder + "/" + materialName + ".mat";
            material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                             Shader.Find("Standard");
                if (shader == null)
                    throw new InvalidOperationException("No compatible Lit shader was found.");

                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, materialPath);
            }

            ConfigureMaterial(material, texture, texturePath, forceTranslucent, tinted);
            EditorUtility.SetDirty(material);
            cache[cacheKey] = material;
            return material;
        }

        private static void ConfigureMaterial(
            Material material,
            Texture2D texture,
            string texturePath,
            bool forceTranslucent,
            bool tinted)
        {
            var textureProperty = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            material.SetTexture(textureProperty, texture);
            material.SetTextureScale(textureProperty, Vector2.one);
            material.SetTextureOffset(textureProperty, Vector2.zero);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", tinted ? BiomeTint : Color.white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", tinted ? BiomeTint : Color.white);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_SpecularHighlights"))
                material.SetFloat("_SpecularHighlights", 0f);

            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            var hasAlpha = importer != null && importer.DoesSourceTextureHaveAlpha();
            if (forceTranslucent)
            {
                SetFloatIfPresent(material, "_Surface", 1f);
                SetFloatIfPresent(material, "_Blend", 0f);
                SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                SetFloatIfPresent(material, "_ZWrite", 0f);
                SetFloatIfPresent(material, "_AlphaClip", 0f);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (hasAlpha)
            {
                SetFloatIfPresent(material, "_Surface", 0f);
                SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.One);
                SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.Zero);
                SetFloatIfPresent(material, "_ZWrite", 1f);
                SetFloatIfPresent(material, "_AlphaClip", 1f);
                SetFloatIfPresent(material, "_Cutoff", 0.5f);
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                SetFloatIfPresent(material, "_Surface", 0f);
                SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.One);
                SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.Zero);
                SetFloatIfPresent(material, "_ZWrite", 1f);
                SetFloatIfPresent(material, "_AlphaClip", 0f);
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = -1;
            }

            // Minecraft animated textures are vertical strips. Until runtime animation is
            // implemented, show the first (top-most) square frame instead of squashing the strip.
            var metadataPath = texturePath + ".mcmeta";
            if (texture.height > texture.width &&
                texture.height % texture.width == 0 &&
                File.Exists(ToAbsoluteProjectPath(metadataPath)))
            {
                var frameScale = (float)texture.width / texture.height;
                material.SetTextureScale(textureProperty, new Vector2(1f, frameScale));
                material.SetTextureOffset(textureProperty, new Vector2(0f, 1f - frameScale));
            }
        }

        private static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static Mesh CreateOrUpdateCubeMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(CubeMeshPath);
            var isNew = mesh == null;
            if (isNew)
                mesh = new Mesh { name = "MinecraftCube" };
            else
                mesh.Clear();

            const float half = 0.5f;
            mesh.vertices = new[]
            {
                // North (-Z)
                new Vector3(-half, -half, -half), new Vector3(-half, half, -half),
                new Vector3(half, half, -half), new Vector3(half, -half, -half),
                // South (+Z)
                new Vector3(half, -half, half), new Vector3(half, half, half),
                new Vector3(-half, half, half), new Vector3(-half, -half, half),
                // East (+X)
                new Vector3(half, -half, -half), new Vector3(half, half, -half),
                new Vector3(half, half, half), new Vector3(half, -half, half),
                // West (-X)
                new Vector3(-half, -half, half), new Vector3(-half, half, half),
                new Vector3(-half, half, -half), new Vector3(-half, -half, -half),
                // Up (+Y)
                new Vector3(-half, half, -half), new Vector3(-half, half, half),
                new Vector3(half, half, half), new Vector3(half, half, -half),
                // Down (-Y)
                new Vector3(-half, -half, half), new Vector3(-half, -half, -half),
                new Vector3(half, -half, -half), new Vector3(half, -half, half)
            };

            mesh.normals = FaceNames.SelectMany((face, index) =>
            {
                var normal = index == 0 ? Vector3.back :
                    index == 1 ? Vector3.forward :
                    index == 2 ? Vector3.right :
                    index == 3 ? Vector3.left :
                    index == 4 ? Vector3.up : Vector3.down;
                return new[] { normal, normal, normal, normal };
            }).ToArray();

            var faceUvs = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 0f)
            };
            mesh.uv = Enumerable.Range(0, FaceNames.Length)
                .SelectMany(_ => faceUvs).ToArray();

            mesh.subMeshCount = FaceNames.Length;
            for (var face = 0; face < FaceNames.Length; face++)
            {
                var first = face * 4;
                mesh.SetTriangles(new[]
                {
                    first, first + 1, first + 2,
                    first, first + 2, first + 3
                }, face, false);
            }

            mesh.RecalculateBounds();
            if (isNew)
                AssetDatabase.CreateAsset(mesh, CubeMeshPath);
            else
                EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static GenerationResult ToResult(SourceData source)
        {
            return new GenerationResult
            {
                sourceVersion = SourceVersion,
                sourceJar = source.JarPath,
                outputFolder = PrefabFolder,
                blockstatesRead = source.BlockstateCount,
                modelsRead = source.ModelCount,
                cubeBlocksFound = source.Definitions.Count,
                skippedNonCube = source.SkippedNonCube,
                skippedMissingTextures = source.SkippedMissingTextures,
                sampleBlocks = source.Definitions.Take(20).Select(item => item.Name).ToArray()
            };
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

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static bool VectorEquals(JArray value, float x, float y, float z)
        {
            return value != null && value.Count == 3 &&
                   Approximately(value[0].Value<float>(), x) &&
                   Approximately(value[1].Value<float>(), y) &&
                   Approximately(value[2].Value<float>(), z);
        }

        private static bool UvEqualsFullFace(JArray value)
        {
            return value.Count == 4 &&
                   Approximately(value[0].Value<float>(), 0f) &&
                   Approximately(value[1].Value<float>(), 0f) &&
                   Approximately(value[2].Value<float>(), 16f) &&
                   Approximately(value[3].Value<float>(), 16f);
        }

        private static bool Approximately(float a, float b)
        {
            return Mathf.Abs(a - b) < 0.0001f;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(character =>
                invalid.Contains(character) ? '_' : character).ToArray());
        }
    }
}
#endif
