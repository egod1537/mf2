using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneToolbar.Editor
{
    internal readonly struct ResolvedSceneAsset
    {
        internal ResolvedSceneAsset(string guid, string path, SceneAsset sceneAsset, bool fileExists)
        {
            Guid = guid;
            Path = path;
            SceneAsset = sceneAsset;
            FileExists = fileExists;
        }

        internal string Guid { get; }
        internal string Path { get; }
        internal SceneAsset SceneAsset { get; }
        internal bool FileExists { get; }
        internal bool IsValid => SceneAsset != null && FileExists;

        internal string AssetName => SceneAsset != null
            ? SceneAsset.name
            : string.IsNullOrEmpty(Path)
                ? string.Empty
                : System.IO.Path.GetFileNameWithoutExtension(Path);
    }

    internal static class SceneAssetUtility
    {
        private const string SceneExtension = ".unity";
        private const string DefaultSceneIconName = "SceneAsset Icon";

        internal static bool CanOpenScenes =>
            !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;

        internal static string GetGuid(SceneAsset sceneAsset)
        {
            if (sceneAsset == null)
            {
                return string.Empty;
            }

            var path = AssetDatabase.GetAssetPath(sceneAsset);
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(SceneExtension, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return AssetDatabase.AssetPathToGUID(path);
        }

        internal static IReadOnlyList<SceneAsset> GetSelectedSceneAssets()
        {
            var scenesByGuid = new Dictionary<string, SceneAsset>(StringComparer.Ordinal);
            foreach (var selectedObject in Selection.objects)
            {
                if (!(selectedObject is SceneAsset sceneAsset))
                {
                    continue;
                }

                var guid = GetGuid(sceneAsset);
                if (!string.IsNullOrEmpty(guid))
                {
                    scenesByGuid[guid] = sceneAsset;
                }
            }

            return scenesByGuid.Values
                .OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal)
                .ToList();
        }

        internal static ResolvedSceneAsset ResolveScene(string sceneGuid)
        {
            if (string.IsNullOrWhiteSpace(sceneGuid))
            {
                return new ResolvedSceneAsset(sceneGuid, string.Empty, null, false);
            }

            var path = AssetDatabase.GUIDToAssetPath(sceneGuid);
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(SceneExtension, StringComparison.OrdinalIgnoreCase))
            {
                return new ResolvedSceneAsset(sceneGuid, path, null, false);
            }

            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            var fileExists = File.Exists(ToAbsolutePath(path));
            return new ResolvedSceneAsset(sceneGuid, path, sceneAsset, fileExists);
        }

        internal static bool IsOnlyOpenScene(string scenePath)
        {
            return SceneManager.sceneCount == 1 && IsActiveScene(scenePath);
        }

        internal static bool IsActiveScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return false;
            }

            var activeScene = SceneManager.GetActiveScene();
            return activeScene.IsValid() && activeScene.isLoaded &&
                   string.Equals(activeScene.path, scenePath, StringComparison.OrdinalIgnoreCase);
        }

        internal static Texture2D ResolveIcon(SceneToolbarEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            switch (entry.IconSource)
            {
                case SceneToolbarIconSource.None:
                    return null;
                case SceneToolbarIconSource.BuiltIn:
                    return TryGetBuiltInIcon(entry.BuiltInIconName, out var builtInIcon)
                        ? builtInIcon
                        : GetDefaultSceneIcon();
                case SceneToolbarIconSource.Custom:
                    return entry.CustomIcon != null ? entry.CustomIcon : GetDefaultSceneIcon();
                default:
                    return GetDefaultSceneIcon();
            }
        }

        internal static bool TryGetBuiltInIcon(string iconName, out Texture2D icon)
        {
            icon = null;
            if (string.IsNullOrWhiteSpace(iconName))
            {
                return false;
            }

            try
            {
                icon = EditorGUIUtility.IconContent(iconName).image as Texture2D;
                return icon != null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[Scene Toolbar] Could not load built-in icon '{iconName}': {exception.Message}");
                return false;
            }
        }

        internal static Texture2D GetDefaultSceneIcon()
        {
            var icon = EditorGUIUtility.IconContent(DefaultSceneIconName).image as Texture2D;
            return icon != null
                ? icon
                : EditorGUIUtility.ObjectContent(null, typeof(SceneAsset)).image as Texture2D;
        }

        internal static Texture2D GetSettingsIcon()
        {
            return EditorGUIUtility.IconContent("SettingsIcon").image as Texture2D;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? assetPath
                : System.IO.Path.Combine(projectRoot, assetPath);
        }
    }
}
