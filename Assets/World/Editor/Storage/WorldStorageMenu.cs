using System;
using MF2.World.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MF2.World.Editor.Storage
{
    internal static class WorldStorageMenu
    {
        private const string DirectoryPreference = "MF2.World.Storage.LastDirectory";

        [MenuItem("Tools/World/Save Active World...", priority = 30)]
        private static void SaveActiveWorld()
        {
            if (!TryGetHost(out var host))
                return;

            var directory = EditorUtility.OpenFolderPanel(
                "Save Active World",
                GetInitialDirectory(),
                string.Empty);
            if (string.IsNullOrEmpty(directory))
                return;

            try
            {
                var result = host.SaveWorld(directory);
                EditorPrefs.SetString(DirectoryPreference, directory);
                Debug.Log(
                    $"Saved {result.DirtySnapshotsAccepted}/{result.SnapshotsWritten} dirty " +
                    $"chunk snapshots across {result.RegionsWritten} regions to '{directory}'.",
                    host);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, host);
                EditorUtility.DisplayDialog("World Save Failed", exception.Message, "OK");
            }
        }

        [MenuItem("Tools/World/Save Active World...", true)]
        private static bool ValidateSaveActiveWorld()
        {
            return !EditorApplication.isPlaying && TryGetHost(out _);
        }

        [MenuItem("Tools/World/Load Active World...", priority = 31)]
        private static void LoadActiveWorld()
        {
            if (!TryGetHost(out var host))
                return;

            var directory = EditorUtility.OpenFolderPanel(
                "Load Active World",
                GetInitialDirectory(),
                string.Empty);
            if (string.IsNullOrEmpty(directory))
                return;

            try
            {
                var snapshot = host.LoadWorld(directory);
                EditorPrefs.SetString(DirectoryPreference, directory);
                EditorUtility.SetDirty(host);
                EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
                SceneView.RepaintAll();
                Debug.Log(
                    $"Loaded {snapshot.ChunkCount} chunks from '{directory}'.",
                    host);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, host);
                EditorUtility.DisplayDialog("World Load Failed", exception.Message, "OK");
            }
        }

        [MenuItem("Tools/World/Load Active World...", true)]
        private static bool ValidateLoadActiveWorld()
        {
            return !EditorApplication.isPlaying && TryGetHost(out _);
        }

        private static bool TryGetHost(out WorldHost host)
        {
            if (Selection.activeGameObject != null)
            {
                host = Selection.activeGameObject.GetComponentInParent<WorldHost>();
                if (host != null && host.isActiveAndEnabled)
                    return true;
            }

            return WorldHost.TryGetActiveHost(out host);
        }

        private static string GetInitialDirectory()
        {
            var stored = EditorPrefs.GetString(DirectoryPreference, string.Empty);
            return string.IsNullOrEmpty(stored) ? Application.persistentDataPath : stored;
        }
    }
}
