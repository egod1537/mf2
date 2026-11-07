using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SceneToolbar.Editor
{
    [InitializeOnLoad]
    internal static class SceneToolbarSettingsRepository
    {
        internal const string SettingsAssetPath =
            "Assets/Plugins/SceneToolbar/Editor/SceneToolbarSettings.asset";

        internal static event Action SettingsChanged;

        static SceneToolbarSettingsRepository()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        internal static SceneToolbarSettings Load()
        {
            return AssetDatabase.LoadAssetAtPath<SceneToolbarSettings>(SettingsAssetPath);
        }

        internal static SceneToolbarSettings GetOrCreate()
        {
            var settings = Load();
            if (settings != null)
            {
                return settings;
            }

            var existingAsset = AssetDatabase.LoadMainAssetAtPath(SettingsAssetPath);
            if (existingAsset != null)
            {
                Debug.LogError(
                    $"[Scene Toolbar] '{SettingsAssetPath}' exists but is not a SceneToolbarSettings asset.");
                return null;
            }

            EnsureAssetFolder("Assets/Plugins/SceneToolbar/Editor");
            settings = ScriptableObject.CreateInstance<SceneToolbarSettings>();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            NotifyChanged();
            return settings;
        }

        internal static bool AddScenes(IEnumerable<SceneAsset> scenes)
        {
            var candidates = (scenes ?? Enumerable.Empty<SceneAsset>())
                .Where(scene => scene != null)
                .Select(scene => new
                {
                    Scene = scene,
                    Path = AssetDatabase.GetAssetPath(scene),
                    Guid = SceneAssetUtility.GetGuid(scene)
                })
                .Where(candidate => !string.IsNullOrEmpty(candidate.Guid))
                .GroupBy(candidate => candidate.Guid, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(candidate => candidate.Path, StringComparer.Ordinal)
                .ToList();

            if (candidates.Count == 0)
            {
                return false;
            }

            var settings = GetOrCreate();
            if (settings == null)
            {
                return false;
            }

            candidates.RemoveAll(candidate => settings.Contains(candidate.Guid));
            if (candidates.Count == 0)
            {
                return false;
            }

            var availableSlots = Math.Max(0, SceneToolbarSettings.MaxEntries - settings.Entries.Count);
            if (availableSlots == 0)
            {
                Debug.LogWarning(
                    $"[Scene Toolbar] Registration is limited to {SceneToolbarSettings.MaxEntries} scenes to prevent main toolbar overflow.");
                return false;
            }

            if (candidates.Count > availableSlots)
            {
                Debug.LogWarning(
                    $"[Scene Toolbar] Only the first {availableSlots} selected scene(s) were added. " +
                    $"Registration is limited to {SceneToolbarSettings.MaxEntries} scenes to prevent main toolbar overflow.");
                candidates = candidates.Take(availableSlots).ToList();
            }

            Undo.RecordObject(settings, "Add Scenes to Scene Toolbar");
            var addedAny = false;
            foreach (var candidate in candidates)
            {
                addedAny |= settings.Add(candidate.Guid);
            }

            if (!addedAny)
            {
                return false;
            }

            SaveAndNotify(settings);
            return true;
        }

        internal static bool RemoveScenes(IEnumerable<SceneAsset> scenes)
        {
            var settings = Load();
            if (settings == null)
            {
                return false;
            }

            var guids = new HashSet<string>(
                (scenes ?? Enumerable.Empty<SceneAsset>())
                    .Where(scene => scene != null)
                    .Select(SceneAssetUtility.GetGuid)
                    .Where(guid => !string.IsNullOrEmpty(guid)),
                StringComparer.Ordinal);

            guids.RemoveWhere(guid => !settings.Contains(guid));
            if (guids.Count == 0)
            {
                return false;
            }

            Undo.RecordObject(settings, "Remove Scenes from Scene Toolbar");
            foreach (var guid in guids)
            {
                settings.Remove(guid);
            }

            SaveAndNotify(settings);
            return true;
        }

        internal static bool Remove(string sceneGuid)
        {
            return Modify(
                "Remove Scene from Scene Toolbar",
                settings => settings.Remove(sceneGuid));
        }

        internal static bool Move(string sceneGuid, int offset)
        {
            return Modify(
                "Reorder Scene Toolbar",
                settings =>
                {
                    var currentIndex = settings.IndexOf(sceneGuid);
                    return settings.Move(currentIndex, currentIndex + offset);
                });
        }

        internal static bool Update(
            string sceneGuid,
            string undoName,
            Action<SceneToolbarEntry> update)
        {
            return Modify(undoName, settings => settings.UpdateEntry(sceneGuid, update));
        }

        private static bool Modify(string undoName, Func<SceneToolbarSettings, bool> modification)
        {
            var settings = Load();
            if (settings == null || modification == null)
            {
                return false;
            }

            Undo.RecordObject(settings, undoName);
            if (!modification(settings))
            {
                return false;
            }

            SaveAndNotify(settings);
            return true;
        }

        private static void SaveAndNotify(SceneToolbarSettings settings)
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            NotifyChanged();
        }

        private static void NotifyChanged()
        {
            SettingsChanged?.Invoke();
            SceneToolbarRefresh.Request();
        }

        private static void OnUndoRedo()
        {
            var settings = Load();
            if (settings != null)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            NotifyChanged();
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            var segments = folderPath.Split('/');
            var currentPath = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var nextPath = $"{currentPath}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, segments[index]);
                }

                currentPath = nextPath;
            }
        }
    }
}
