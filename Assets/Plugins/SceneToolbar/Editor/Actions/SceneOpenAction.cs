using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SceneToolbar.Editor
{
    internal static class SceneOpenAction
    {
        internal static bool Open(string sceneGuid)
        {
            var resolvedScene = SceneAssetUtility.ResolveScene(sceneGuid);
            if (!resolvedScene.IsValid)
            {
                Debug.LogError(
                    $"[Scene Toolbar] Scene GUID '{sceneGuid}' no longer resolves to a valid SceneAsset.");
                return false;
            }

            if (!SceneAssetUtility.CanOpenScenes)
            {
                return false;
            }

            if (SceneAssetUtility.IsOnlyOpenScene(resolvedScene.Path))
            {
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            try
            {
                var openedScene = EditorSceneManager.OpenScene(resolvedScene.Path, OpenSceneMode.Single);
                if (!openedScene.IsValid())
                {
                    Debug.LogError($"[Scene Toolbar] Unity did not open scene '{resolvedScene.Path}'.");
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[Scene Toolbar] Failed to open scene '{resolvedScene.Path}': {exception.Message}");
                return false;
            }
        }
    }
}
