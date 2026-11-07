using System;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace SceneToolbar.Editor
{
    internal static class SceneToolbarRefresh
    {
        internal const string SettingsPath = "Scene Toolbar/Settings";
        internal const string ScenesPath = "Scene Toolbar/Scenes";

        private static bool refreshScheduled;

        internal static void Request()
        {
            if (refreshScheduled)
            {
                return;
            }

            refreshScheduled = true;
            EditorApplication.delayCall -= RefreshNow;
            EditorApplication.delayCall += RefreshNow;
        }

        private static void RefreshNow()
        {
            refreshScheduled = false;
            EditorApplication.delayCall -= RefreshNow;

            RefreshPath(SettingsPath);
            RefreshPath(ScenesPath);
        }

        private static void RefreshPath(string toolbarPath)
        {
            try
            {
                MainToolbar.Refresh(toolbarPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[Scene Toolbar] Toolbar refresh failed for '{toolbarPath}': {exception.Message}");
            }
        }
    }
}
