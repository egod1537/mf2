using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneToolbar.Editor
{
    [InitializeOnLoad]
    internal static class SceneToolbarElements
    {
        static SceneToolbarElements()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        [MainToolbarElement(
            SceneToolbarRefresh.SettingsPath,
            defaultDockPosition = MainToolbarDockPosition.Middle,
            defaultDockIndex = 101)]
        internal static MainToolbarElement CreateSettingsButton()
        {
            var settingsIcon = SceneAssetUtility.GetSettingsIcon();
            var settingsContent = settingsIcon != null
                ? new MainToolbarContent(settingsIcon, "Open Scene Toolbar settings")
                : new MainToolbarContent("Scenes", "Open Scene Toolbar settings");
            return new MainToolbarButton(settingsContent, SceneToolbarSettingsWindow.Open);
        }

        [MainToolbarElement(
            SceneToolbarRefresh.ScenesPath,
            defaultDockPosition = MainToolbarDockPosition.Middle,
            defaultDockIndex = 102)]
        internal static IEnumerable<MainToolbarElement> CreateSceneButtons()
        {
            var settings = SceneToolbarSettingsRepository.Load();
            if (settings != null)
            {
                var displayedEntryCount = 0;
                foreach (var entry in settings.Entries)
                {
                    if (displayedEntryCount >= SceneToolbarSettings.MaxEntries)
                    {
                        yield break;
                    }

                    if (entry == null || string.IsNullOrWhiteSpace(entry.SceneGuid))
                    {
                        continue;
                    }

                    displayedEntryCount++;

                    var sceneGuid = entry.SceneGuid;
                    var resolvedScene = SceneAssetUtility.ResolveScene(sceneGuid);
                    var isActiveScene = resolvedScene.IsValid &&
                                        SceneAssetUtility.IsActiveScene(resolvedScene.Path);
                    var isOnlyOpenScene = resolvedScene.IsValid &&
                                          SceneAssetUtility.IsOnlyOpenScene(resolvedScene.Path);
                    var canOpen = resolvedScene.IsValid &&
                                  SceneAssetUtility.CanOpenScenes &&
                                  !isOnlyOpenScene;
                    var button = new MainToolbarButton(
                        CreateSceneContent(entry, resolvedScene, isActiveScene, isOnlyOpenScene),
                        () => SceneOpenAction.Open(sceneGuid))
                    {
                        enabled = canOpen
                    };

                    yield return button;
                }
            }
        }

        private static MainToolbarContent CreateSceneContent(
            SceneToolbarEntry entry,
            ResolvedSceneAsset resolvedScene,
            bool isActiveScene,
            bool isOnlyOpenScene)
        {
            var label = !string.IsNullOrWhiteSpace(entry.DisplayName)
                ? entry.DisplayName
                : !string.IsNullOrWhiteSpace(resolvedScene.AssetName)
                    ? resolvedScene.AssetName
                    : "Missing Scene";
            var tooltip = CreateTooltip(label, resolvedScene, isActiveScene, isOnlyOpenScene);
            var icon = SceneAssetUtility.ResolveIcon(entry);

            switch (entry.DisplayMode)
            {
                case SceneToolbarDisplayMode.IconOnly:
                    icon ??= SceneAssetUtility.GetDefaultSceneIcon();
                    return icon != null
                        ? new MainToolbarContent(icon, tooltip)
                        : new MainToolbarContent(label, tooltip);
                case SceneToolbarDisplayMode.TextOnly:
                    return new MainToolbarContent(label, tooltip);
                case SceneToolbarDisplayMode.IconAndText:
                default:
                    return icon != null
                        ? new MainToolbarContent(label, icon, tooltip)
                        : new MainToolbarContent(label, tooltip);
            }
        }

        private static string CreateTooltip(
            string label,
            ResolvedSceneAsset resolvedScene,
            bool isActiveScene,
            bool isOnlyOpenScene)
        {
            if (!resolvedScene.IsValid)
            {
                return $"{label}\nMissing SceneAsset. Open settings to remove this entry.";
            }

            if (!SceneAssetUtility.CanOpenScenes)
            {
                return $"{label}\nScene switching is disabled during Play Mode transitions.";
            }

            if (isOnlyOpenScene)
            {
                return $"{label}\nCurrent scene: {resolvedScene.Path}";
            }

            if (isActiveScene)
            {
                return $"{label}\nActive scene. Click to reopen it as the only open scene.\n{resolvedScene.Path}";
            }

            return $"Open {label}\n{resolvedScene.Path}";
        }

        private static void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
        {
            SceneToolbarRefresh.Request();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            SceneToolbarRefresh.Request();
        }

        private static void OnProjectChanged()
        {
            SceneToolbarRefresh.Request();
        }
    }
}
