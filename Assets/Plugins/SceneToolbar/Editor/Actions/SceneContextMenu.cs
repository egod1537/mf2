using System.Linq;
using UnityEditor;

namespace SceneToolbar.Editor
{
    internal static class SceneContextMenu
    {
        private const string AddMenuPath = "Assets/Scene Toolbar/Add to Toolbar";
        private const string RemoveMenuPath = "Assets/Scene Toolbar/Remove from Toolbar";
        private const string OpenSettingsMenuPath = "Assets/Scene Toolbar/Open Settings";

        [MenuItem(AddMenuPath, false, 2000)]
        private static void AddSelectedScenes()
        {
            SceneToolbarSettingsRepository.AddScenes(SceneAssetUtility.GetSelectedSceneAssets());
        }

        [MenuItem(AddMenuPath, true)]
        private static bool ValidateAddSelectedScenes()
        {
            var selectedScenes = SceneAssetUtility.GetSelectedSceneAssets();
            if (selectedScenes.Count == 0 || selectedScenes.Count != Selection.objects.Length)
            {
                return false;
            }

            var settings = SceneToolbarSettingsRepository.Load();
            return settings == null ||
                   settings.Entries.Count < SceneToolbarSettings.MaxEntries &&
                   selectedScenes.Any(scene => !settings.Contains(SceneAssetUtility.GetGuid(scene)));
        }

        [MenuItem(RemoveMenuPath, false, 2001)]
        private static void RemoveSelectedScenes()
        {
            SceneToolbarSettingsRepository.RemoveScenes(SceneAssetUtility.GetSelectedSceneAssets());
        }

        [MenuItem(RemoveMenuPath, true)]
        private static bool ValidateRemoveSelectedScenes()
        {
            var selectedScenes = SceneAssetUtility.GetSelectedSceneAssets();
            if (selectedScenes.Count == 0 || selectedScenes.Count != Selection.objects.Length)
            {
                return false;
            }

            var settings = SceneToolbarSettingsRepository.Load();
            return settings != null && selectedScenes.Any(scene => settings.Contains(SceneAssetUtility.GetGuid(scene)));
        }

        [MenuItem(OpenSettingsMenuPath, false, 2002)]
        private static void OpenSettings()
        {
            SceneToolbarSettingsWindow.Open();
        }
    }
}
