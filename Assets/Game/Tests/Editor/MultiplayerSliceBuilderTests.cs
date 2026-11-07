using System.Collections.Generic;
using System.Linq;
using FishNet.Managing;
using FishNet.Object;
using MF2.Entities.Player;
using MF2.Game.Lifecycle;
using MF2.Game.Networking;
using MF2.Game.Presentation;
using MF2.Game.UI;
using MF2.World.Core;
using MF2.World.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MF2.Game.Tests.Editor
{
    public sealed class MultiplayerSliceBuilderTests
    {
        [Test]
        public void BuilderOutput_PreservesAssetGuidsAndPlayerPrefabWiring()
        {
            Assert.That(
                AssetDatabase.AssetPathToGUID("Assets/Entities/Player/BlockPlayer.prefab"),
                Is.EqualTo("7b23535991dbc1949bbbebe6af6862f5"));
            Assert.That(
                AssetDatabase.AssetPathToGUID("Assets/Entities/Player/OfflineBlockPlayer.prefab"),
                Is.EqualTo("d51da634c07718547810e2a4661878dd"));
            Assert.That(
                AssetDatabase.AssetPathToGUID("Assets/Scenes/Title.unity"),
                Is.EqualTo("41a09bd62234c894dbf7df3277e0a78b"));
            Assert.That(
                AssetDatabase.AssetPathToGUID("Assets/Scenes/Outgame.unity"),
                Is.EqualTo("ffc4c70f31979df449868c25ae13c14d"));
            Assert.That(
                AssetDatabase.AssetPathToGUID("Assets/Scenes/Test.unity"),
                Is.EqualTo("283a67ed0bed22a439c227eb4cf16cef"));
            Assert.That(
                AssetDatabase.AssetPathToGUID("Assets/Scenes/Ingame.unity"),
                Is.EqualTo("650dc6c768eca234e9c839fbd05ecbdc"));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Entities/Player/BlockPlayer.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CapsuleCollider>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BlockPlayerController>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BlockPlayerInputSource>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BlockPlayerView>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<EntityViewBinder>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<PlayerPresentationBindingGate>(), Is.Not.Null);
        }

        [Test]
        public void BuilderOutput_TitleAndOutgameKeepScopeNetworkAndUiWiring()
        {
            var title = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Title.unity");
            try
            {
                var bootstrap = FindInScene<NetworkBootstrap>(title);
                Assert.That(bootstrap, Is.Not.Null);
                var root = bootstrap.gameObject;
                Assert.That(root.GetComponent<ApplicationLifetimeScope>(), Is.Not.Null);
                Assert.That(root.GetComponent<ServerSessionCoordinator>(), Is.Not.Null);
                Assert.That(root.GetComponent<ClientPresentationCoordinator>(), Is.Not.Null);
                Assert.That(root.GetComponent<GameCommandNetworkAdapter>(), Is.Not.Null);
                Assert.That(root.GetComponent<PlayerReplicationAdapter>(), Is.Not.Null);
                Assert.That(root.GetComponent<WorldReplicationAdapter>(), Is.Not.Null);
                Assert.That(root.GetComponent<ServerWorldCollisionBridge>(), Is.Not.Null);
                Assert.That(root.GetComponent<SessionController>(), Is.Not.Null);
                Assert.That(root.GetComponent<PlayerSpawner>(), Is.Not.Null);
                Assert.That(FindInScene<SceneLifetimeScope>(title), Is.Not.Null);
                Assert.That(FindInScene<EventSystem>(title), Is.Not.Null);

                var manager = root.GetComponent<NetworkManager>();
                Assert.That(manager, Is.Not.Null);
                Assert.That(manager.SpawnablePrefabs, Is.Not.Null);
                var player = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Entities/Player/BlockPlayer.prefab").GetComponent<NetworkObject>();
                Assert.That(
                    Enumerable.Range(0, manager.SpawnablePrefabs.GetObjectCount())
                        .Select(index => manager.SpawnablePrefabs.GetObject(true, index))
                        .Any(candidate => candidate == player),
                    Is.True,
                    "The generated NetworkManager must retain BlockPlayer in its prefab registry.");

                var spawner = new SerializedObject(root.GetComponent<PlayerSpawner>());
                Assert.That(spawner.FindProperty("playerPrefab").objectReferenceValue, Is.SameAs(player));
                CollectionAssert.IsSubsetOf(
                    new[] { "StartGame", "QuitGame" },
                    PersistentButtonMethods(title));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(title);
            }

            var outgame = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Outgame.unity");
            try
            {
                var controller = FindInScene<OutgameController>(outgame);
                Assert.That(controller, Is.Not.Null);
                Assert.That(FindInScene<SceneLifetimeScope>(outgame), Is.Not.Null);
                Assert.That(FindInScene<EventSystem>(outgame), Is.Not.Null);
                var serialized = new SerializedObject(controller);
                foreach (var propertyName in new[]
                         {
                             "hostInput", "portInput", "statusText", "rosterText", "hostButton",
                             "joinButton", "disconnectButton", "startTestButton", "startIngameButton"
                         })
                {
                    Assert.That(
                        serialized.FindProperty(propertyName).objectReferenceValue,
                        Is.Not.Null,
                        propertyName);
                }
                CollectionAssert.IsSubsetOf(
                    new[] { "Host", "Join", "Disconnect", "StartTest", "StartIngame" },
                    PersistentButtonMethods(outgame));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(outgame);
            }
        }

        [TestCase("Assets/Scenes/Test.unity", 4)]
        [TestCase("Assets/Scenes/Ingame.unity", 2)]
        public void BuilderOutput_GameScenesKeepPresentationOnlyWorldWiring(
            string scenePath,
            int expectedSpawnPoints)
        {
            var scene = EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                var world = FindInScene<WorldHost>(scene);
                Assert.That(world, Is.Not.Null);
                Assert.That(world.GetComponent<WorldViewBridge>(), Is.Not.Null);
                Assert.That(FindInScene<WorldCommandDebugInput>(scene), Is.Not.Null);
                Assert.That(FindInScene<NetworkSceneHud>(scene), Is.Not.Null);
                Assert.That(FindInScene<SceneLifetimeScope>(scene), Is.Not.Null);
                Assert.That(FindInScene<EventSystem>(scene), Is.Not.Null);
                Assert.That(FindAllInScene<SpawnPoint>(scene).Count, Is.EqualTo(expectedSpawnPoints));

                var serializedWorld = new SerializedObject(world);
                foreach (var propertyName in new[]
                         {
                             "blockRegistryManifest", "blockRenderManifest", "chunkMaterialSet"
                         })
                {
                    Assert.That(
                        serializedWorld.FindProperty(propertyName).objectReferenceValue,
                        Is.Not.Null,
                        propertyName);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void BuilderOutput_IngameAndOutgameMatchMinecraftMapRenderingStyle()
        {
            const string profilePath =
                "Assets/Scenes/MinecraftBlockMap/CM Isometric Camera Profile.asset";
            const string materialPath =
                "Assets/Minecraft/Materials/MinecraftBlockAtlas_ProPixelizer.mat";
            const string materialSetPath =
                "Assets/World/Rendering/ChunkMaterialSetProPixelizer.asset";

            var materialSet = AssetDatabase.LoadAssetAtPath<ChunkMaterialSet>(materialSetPath);
            Assert.That(materialSet, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(materialSet.Opaque), Is.EqualTo(materialPath));
            Assert.That(materialSet.Cutout, Is.SameAs(materialSet.Opaque));
            Assert.That(materialSet.Transparent, Is.SameAs(materialSet.Opaque));

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Entities/Player/BlockPlayer.prefab");
            AssertMinecraftMapCamera(playerPrefab.GetComponentInChildren<Camera>(true));
            AssertProPixelizerVisual(playerPrefab, materialPath);

            var offlinePlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Entities/Player/OfflineBlockPlayer.prefab");
            AssertProPixelizerVisual(offlinePlayerPrefab, materialPath);

            foreach (var scenePath in new[]
                     {
                         "Assets/Scenes/Ingame.unity",
                         "Assets/Scenes/Outgame.unity"
                     })
            {
                var scene = EditorSceneManager.OpenPreviewScene(scenePath);
                try
                {
                    AssertMinecraftMapCamera(FindInScene<Camera>(scene));
                    var volume = FindInScene<Volume>(scene);
                    Assert.That(volume, Is.Not.Null, scenePath);
                    Assert.That(volume.isGlobal, Is.True, scenePath);
                    Assert.That(volume.weight, Is.EqualTo(1f), scenePath);
                    Assert.That(
                        AssetDatabase.GetAssetPath(volume.sharedProfile),
                        Is.EqualTo(profilePath),
                        scenePath);

                    if (scenePath.EndsWith("Ingame.unity"))
                    {
                        var world = FindInScene<WorldHost>(scene);
                        var serializedWorld = new SerializedObject(world);
                        Assert.That(
                            AssetDatabase.GetAssetPath(
                                serializedWorld.FindProperty("chunkMaterialSet")
                                    .objectReferenceValue),
                            Is.EqualTo(materialSetPath));
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }

        private static T FindInScene<T>(Scene scene) where T : Component =>
            FindAllInScene<T>(scene).FirstOrDefault();

        private static List<T> FindAllInScene<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToList();

        private static string[] PersistentButtonMethods(Scene scene)
        {
            var methods = new List<string>();
            foreach (var button in FindAllInScene<Button>(scene))
            {
                for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    methods.Add(button.onClick.GetPersistentMethodName(i));
            }
            return methods.ToArray();
        }

        private static void AssertMinecraftMapCamera(Camera camera)
        {
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(camera.backgroundColor.r, Is.EqualTo(0.43f).Within(0.001f));
            Assert.That(camera.backgroundColor.g, Is.EqualTo(0.69f).Within(0.001f));
            Assert.That(camera.backgroundColor.b, Is.EqualTo(0.92f).Within(0.001f));
            Assert.That(camera.orthographic, Is.True);
            Assert.That(camera.orthographicSize, Is.EqualTo(8.5f).Within(0.001f));
            Assert.That(camera.nearClipPlane, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(camera.farClipPlane, Is.EqualTo(160f).Within(0.001f));
            Assert.That(camera.allowHDR, Is.False);

            var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            Assert.That(cameraData, Is.Not.Null);
            Assert.That(cameraData.renderPostProcessing, Is.True);
            Assert.That((cameraData.volumeLayerMask.value & 1) != 0, Is.True);
        }

        private static void AssertProPixelizerVisual(GameObject prefab, string materialPath)
        {
            Assert.That(prefab, Is.Not.Null);
            var visual = prefab.transform.Find("Visual");
            Assert.That(visual, Is.Not.Null, prefab.name);

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty, prefab.name);
            foreach (var renderer in renderers)
            foreach (var material in renderer.sharedMaterials)
            {
                Assert.That(material, Is.Not.Null, renderer.name);
                Assert.That(
                    AssetDatabase.GetAssetPath(material),
                    Is.EqualTo(materialPath),
                    renderer.name);
                Assert.That(
                    material.shader.name,
                    Is.EqualTo("ProPixelizer/ProPixelizerUberShader"),
                    renderer.name);
            }
        }
    }
}
