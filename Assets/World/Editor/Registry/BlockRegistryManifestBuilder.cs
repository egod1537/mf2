using System;
using System.Linq;
using MF2.World.Registry;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace MF2.World.Editor.Registry
{
    public static class BlockRegistryManifestBuilder
    {
        public const string MinecraftPrefabFolder = "Assets/Entities/Blocks";
        public const string ManifestPath = "Assets/World/Registry/BlockRegistryManifest.asset";

        [MenuItem("Tools/Minecraft/Rebuild Block Registry Manifest")]
        public static BlockRegistryManifest RebuildFromMinecraftPrefabs()
        {
            var prefabPaths = AssetDatabase
                .FindAssets("t:Prefab", new[] { MinecraftPrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (prefabPaths.Length == 0)
                throw new InvalidOperationException("No Minecraft block prefabs were found.");

            var entries = new BlockRegistryManifest.Entry[prefabPaths.Length];
            for (var i = 0; i < prefabPaths.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[i]);
                if (prefab == null)
                    throw new InvalidOperationException("Failed to load block prefab: " + prefabPaths[i]);

                var canonicalName = "minecraft:" + prefab.name;
                StableBlockId.ValidateCanonicalName(canonicalName);
                entries[i] = new BlockRegistryManifest.Entry(canonicalName, prefab);
            }

            // Build once before touching the asset so duplicates, ID zero, and collisions fail
            // atomically and name validation is identical to the runtime table.
            var names = entries.Select(entry => entry.CanonicalName).ToArray();
            using (var validationRegistry = new BlockRegistry(names, Allocator.Temp))
            {
                if (validationRegistry.Count != entries.Length)
                    throw new InvalidOperationException("Registry validation returned an incomplete table.");
            }

            EnsureFolder("Assets/World/Registry");
            var manifest = AssetDatabase.LoadAssetAtPath<BlockRegistryManifest>(ManifestPath);
            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<BlockRegistryManifest>();
                AssetDatabase.CreateAsset(manifest, ManifestPath);
            }

            manifest.ReplaceEntries(entries);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssetIfDirty(manifest);
            Debug.Log(
                $"Block registry manifest rebuilt with {entries.Length} canonical entries " +
                $"using ID scheme {StableBlockId.SchemeVersion}.",
                manifest);
            return manifest;
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
