using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using MF2.Entities.Player;
using MF2.Game.Lifecycle;
using MF2.Game.Networking;
using MF2.Game.Presentation;
using MF2.Game.UI;
using MF2.World.Core;
using MF2.World.Registry;
using MF2.World.Rendering;
using ProPixelizer;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MF2.Editor
{
    public static class MultiplayerSliceBuilder
    {
        private const string PlayerPrefabPath = "Assets/Entities/Player/BlockPlayer.prefab";
        private const string OfflinePlayerPrefabPath = "Assets/Entities/Player/OfflineBlockPlayer.prefab";
        private const string PlayerPhysicsMaterialPath =
            "Assets/Entities/Player/PlayerNoFriction.physicMaterial";
        private const string TitlePath = "Assets/Scenes/Title.unity";
        private const string OutgamePath = "Assets/Scenes/Outgame.unity";
        private const string TestPath = "Assets/Scenes/Test.unity";
        private const string IngamePath = "Assets/Scenes/Ingame.unity";
        private const string BlockRegistryManifestPath = "Assets/World/Registry/BlockRegistryManifest.asset";
        private const string BlockRenderManifestPath = "Assets/World/Rendering/BlockRenderManifest.asset";
        private const string ProPixelizerChunkMaterialSetPath =
            "Assets/World/Rendering/ChunkMaterialSetProPixelizer.asset";
        private const string BlockAtlasPath =
            "Assets/Minecraft/Atlas/minecraft_blocks_atlas.png";
        private const string ProPixelizerBlockMaterialPath =
            "Assets/Minecraft/Materials/MinecraftBlockAtlas_ProPixelizer.mat";
        private const string MinecraftMapVolumeProfilePath =
            "Assets/Scenes/MinecraftBlockMap/CM Isometric Camera Profile.asset";
        private const float MinecraftMapOrthographicSize = 8.5f;
        private const float MinecraftMapLensScale = 1.25f;
        private const float ProPixelizerWorldSpacePixelSize = 0.016f;
        private const float PixelStableGameOrthographicSize =
            MinecraftMapOrthographicSize / MinecraftMapLensScale;

        [MenuItem("MF2/Build Multiplayer Test Slice")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("플레이 모드에서는 씬을 생성할 수 없습니다.");

            ConfigureProPixelizerRenderingAssets();
            ConfigurePlayerPrefab();
            ConfigureOfflinePlayerPrefab();
            CreateTitleScene();
            CreateOutgameScene();
            CreateGameScene(TestPath, "Test", true);
            CreateGameScene(IngamePath, "Ingame", false);
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(TitlePath, OpenSceneMode.Single);
            Debug.Log("[MF2] FishNet multiplayer test slice generated successfully.");
        }

        private static void ConfigurePlayerPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                foreach (var characterController in root.GetComponents<CharacterController>())
                    UnityEngine.Object.DestroyImmediate(characterController, true);

                var capsule = GetOrAdd<CapsuleCollider>(root);
                capsule.height = 1.92f;
                capsule.radius = 0.32f;
                capsule.center = new Vector3(0f, 0.96f, 0f);
                capsule.sharedMaterial = LoadRequiredAsset<PhysicsMaterial>(
                    PlayerPhysicsMaterialPath);

                var rigidbody = GetOrAdd<Rigidbody>(root);
                rigidbody.mass = 1f;
                rigidbody.useGravity = true;
                rigidbody.isKinematic = false;
                rigidbody.interpolation = RigidbodyInterpolation.None;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rigidbody.constraints = RigidbodyConstraints.FreezeRotation;

                var networkObject = GetOrAdd<NetworkObject>(root);
                networkObject.SetIsSpawnable(true);
                var visual = root.transform.Find("Visual");
                var networkSerialized = new SerializedObject(networkObject);
                networkSerialized.FindProperty("_enablePrediction").boolValue = true;
                networkSerialized.FindProperty("_predictionType").enumValueIndex = 1;
                networkSerialized.FindProperty("_enableStateForwarding").boolValue = true;
                networkSerialized.FindProperty("_graphicalObject").objectReferenceValue = visual;
                networkSerialized.FindProperty("_ownerInterpolation").intValue = 2;
                networkSerialized.FindProperty("_spectatorInterpolation").intValue = 3;
                networkSerialized.ApplyModifiedPropertiesWithoutUndo();

                var input = GetOrAdd<BlockPlayerInputSource>(root);
                var view = GetOrAdd<BlockPlayerView>(root);
                var binder = GetOrAdd<EntityViewBinder>(root);
                var bindingGate = GetOrAdd<PlayerPresentationBindingGate>(root);
                var controller = GetOrAdd<BlockPlayerController>(root);
                ConfigureProPixelizerVisual(visual);
                controller.ConfigureMvc(input, view, binder, bindingGate);
                binder.Configure(view);
                bindingGate.Configure(binder);
                var facing = view.ConfigureVisual(visual.gameObject, rigidbody);
                view.ConfigureRig(
                    facing.Find("Left Arm Pivot"),
                    facing.Find("Right Arm Pivot"),
                    facing.Find("Left Leg Pivot"),
                    facing.Find("Right Leg Pivot"));

                var oldRig = root.transform.Find("OwnerCameraRig");
                if (oldRig != null)
                    UnityEngine.Object.DestroyImmediate(oldRig.gameObject);
                var cameraRig = new GameObject("OwnerCameraRig");
                cameraRig.transform.SetParent(root.transform, false);
                cameraRig.transform.localPosition = new Vector3(-8f, 8f, -8f);
                cameraRig.transform.localRotation = new Quaternion(
                    0.27984816f,
                    0.3647052f,
                    -0.1159169f,
                    0.8804763f);
                var ownerCamera = cameraRig.AddComponent<Camera>();
                ConfigurePixelStableGameCamera(ownerCamera);
                ownerCamera.tag = "MainCamera";
                var occlusionDither = cameraRig.AddComponent<PlayerOcclusionDither>();
                occlusionDither.Configure(root.transform);
                var listener = cameraRig.AddComponent<AudioListener>();
                controller.ConfigureOwnerCamera(ownerCamera, listener);
                cameraRig.SetActive(false);

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.ImportAsset(PlayerPrefabPath, ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureOfflinePlayerPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(OfflinePlayerPrefabPath);
            try
            {
                foreach (var binder in root.GetComponents<EntityViewBinder>())
                    UnityEngine.Object.DestroyImmediate(binder, true);

                var capsule = GetOrAdd<CapsuleCollider>(root);
                capsule.sharedMaterial = LoadRequiredAsset<PhysicsMaterial>(
                    PlayerPhysicsMaterialPath);
                var body = GetOrAdd<Rigidbody>(root);
                var input = GetOrAdd<BlockPlayerInputSource>(root);
                var view = GetOrAdd<BlockPlayerView>(root);
                var controller = GetOrAdd<OfflineBlockPlayerController>(root);
                controller.ConfigureMvc(input, view);

                var visual = root.transform.Find("Visual");
                if (visual != null)
                {
                    ConfigureProPixelizerVisual(visual);
                    var facing = view.ConfigureVisual(visual.gameObject, body);
                    view.ConfigureRig(
                        facing.Find("Left Arm Pivot"),
                        facing.Find("Right Arm Pivot"),
                        facing.Find("Left Leg Pivot"),
                        facing.Find("Right Leg Pivot"));
                }

                var camera = root.GetComponentInChildren<Camera>(true);
                if (camera != null)
                {
                    var listener = camera.GetComponent<AudioListener>();
                    view.ConfigureOwnerCamera(camera, listener);
                    input.ConfigureMovementCamera(camera);
                }

                PrefabUtility.SaveAsPrefabAsset(root, OfflinePlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.ImportAsset(OfflinePlayerPrefabPath, ImportAssetOptions.ForceUpdate);
        }

        private static void CreateTitleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Title";

            var networkRoot = new GameObject("NetworkBootstrap");
            networkRoot.AddComponent<Tugboat>();
            var applicationScope = networkRoot.AddComponent<ApplicationLifetimeScope>();
            applicationScope.ConfigureBlockRegistry(
                LoadRequiredAsset<BlockRegistryManifest>(BlockRegistryManifestPath));
            networkRoot.AddComponent<ServerSessionCoordinator>();
            GetOrAdd<ClientPresentationCoordinator>(networkRoot);
            networkRoot.AddComponent<GameCommandNetworkAdapter>();
            networkRoot.AddComponent<PlayerReplicationAdapter>();
            networkRoot.AddComponent<WorldReplicationAdapter>();
            networkRoot.AddComponent<ServerWorldCollisionBridge>();
            var bootstrap = networkRoot.AddComponent<NetworkBootstrap>();
            var manager = networkRoot.GetComponent<NetworkManager>();
            manager.SpawnablePrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>("Assets/DefaultPrefabObjects.asset");
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponent<NetworkObject>();
            networkRoot.GetComponent<PlayerSpawner>().Configure(playerPrefab);

            var controller = new GameObject("TitleController").AddComponent<TitleController>();
            var panel = CreateCanvasWithPanel("TitleCanvas", new Vector2(680f, 420f));
            CreateText(panel, "MF2 MULTIPLAYER", 42, TextAnchor.MiddleCenter, 90f);
            CreateText(panel, "FishNet Rigidbody Prediction Test", 20, TextAnchor.MiddleCenter, 55f);
            var start = CreateButton(panel, "Start", 64f);
            var quit = CreateButton(panel, "Quit", 64f);
            UnityEventTools.AddPersistentListener(start.onClick, controller.StartGame);
            UnityEventTools.AddPersistentListener(quit.onClick, controller.QuitGame);
            CreateEventSystem();
            CreateSceneScope(scene);

            EditorSceneManager.SaveScene(scene, TitlePath);
        }

        private static void CreateOutgameScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Outgame";

            var camera = CreateMinecraftMapCamera("Minecraft Map Camera");
            camera.transform.position = new Vector3(-8f, 9.68f, -15f);
            camera.transform.rotation = new Quaternion(
                0.27984816f,
                0.3647052f,
                -0.1159169f,
                0.8804763f);
            CreateMinecraftMapPostProcessing();

            var controller = new GameObject("OutgameController").AddComponent<OutgameController>();
            var panel = CreateCanvasWithPanel("OutgameCanvas", new Vector2(820f, 780f));
            CreateText(panel, "OUTGAME / LOBBY", 36, TextAnchor.MiddleCenter, 70f);
            var status = CreateText(panel, "Disconnected", 22, TextAnchor.MiddleLeft, 70f);

            var addressRow = CreateHorizontalRow(panel, "AddressRow", 64f);
            var hostInput = CreateInput(addressRow, "127.0.0.1", "IP / hostname", 470f);
            var portInput = CreateInput(addressRow, "7770", "Port", 210f);

            var connectRow = CreateHorizontalRow(panel, "ConnectRow", 64f);
            var host = CreateButton(connectRow, "Host", 64f, 210f);
            var join = CreateButton(connectRow, "Join", 64f, 210f);
            var disconnect = CreateButton(connectRow, "Disconnect / Back", 64f, 280f);
            var roster = CreateText(panel, "Players: 0\nConnection IDs: -", 21, TextAnchor.UpperLeft, 115f);

            var sceneRow = CreateHorizontalRow(panel, "SceneRow", 64f);
            var test = CreateButton(sceneRow, "Start Test", 64f, 330f);
            var ingame = CreateButton(sceneRow, "Start Ingame", 64f, 330f);

            UnityEventTools.AddPersistentListener(host.onClick, controller.Host);
            UnityEventTools.AddPersistentListener(join.onClick, controller.Join);
            UnityEventTools.AddPersistentListener(disconnect.onClick, controller.Disconnect);
            UnityEventTools.AddPersistentListener(test.onClick, controller.StartTest);
            UnityEventTools.AddPersistentListener(ingame.onClick, controller.StartIngame);

            SetObjectReference(controller, "hostInput", hostInput);
            SetObjectReference(controller, "portInput", portInput);
            SetObjectReference(controller, "statusText", status);
            SetObjectReference(controller, "rosterText", roster);
            SetObjectReference(controller, "hostButton", host);
            SetObjectReference(controller, "joinButton", join);
            SetObjectReference(controller, "disconnectButton", disconnect);
            SetObjectReference(controller, "startTestButton", test);
            SetObjectReference(controller, "startIngameButton", ingame);
            CreateEventSystem();
            CreateSceneScope(scene);

            EditorSceneManager.SaveScene(scene, OutgamePath);
        }

        private static void CreateGameScene(string path, string displayName, bool expandedTestScene)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = displayName;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = expandedTestScene ? new Vector3(4f, 1f, 4f) : new Vector3(2f, 1f, 2f);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            ConfigureGameSceneLighting(light);

            var overview = new GameObject("Overview Camera").AddComponent<Camera>();
            overview.transform.position = new Vector3(10f, 12f, -10f);
            overview.transform.rotation = Quaternion.Euler(36f, -45f, 0f);
            overview.fieldOfView = 55f;
            overview.tag = "MainCamera";
            var chunkMaterialSet = ConfigureGameSceneRendering(overview);

            var spawnPositions = expandedTestScene
                ? new[] { new Vector3(-4f, 0.1f, -3f), new Vector3(4f, 0.1f, 3f), new Vector3(-4f, 0.1f, 3f), new Vector3(4f, 0.1f, -3f) }
                : new[] { new Vector3(-2f, 0.1f, 0f), new Vector3(2f, 0.1f, 0f) };
            for (var i = 0; i < spawnPositions.Length; i++)
            {
                var point = new GameObject($"Spawn Point {i + 1}");
                point.transform.position = spawnPositions[i];
                point.AddComponent<SpawnPoint>();
            }

            var worldHost = new GameObject("World").AddComponent<WorldHost>();
            worldHost.gameObject.AddComponent<WorldViewBridge>();
            worldHost.ConfigureAssets(
                LoadRequiredAsset<BlockRegistryManifest>(BlockRegistryManifestPath),
                LoadRequiredAsset<BlockRenderManifest>(BlockRenderManifestPath),
                chunkMaterialSet);

            var debugInput = new GameObject("WorldCommandDebugInput")
                .AddComponent<WorldCommandDebugInput>();
            debugInput.Configure(
                LoadRequiredAsset<BlockRegistryManifest>(BlockRegistryManifestPath),
                Vector3Int.zero);

            var hud = new GameObject("NetworkSceneHud").AddComponent<NetworkSceneHud>();
            var canvas = CreateCanvas("HUD Canvas");
            var status = CreateAnchoredText(canvas.transform, "Status", new Vector2(18f, -18f), new Vector2(520f, 96f));
            var disconnect = CreateAnchoredButton(canvas.transform, "Disconnect / Lobby", new Vector2(-18f, -18f), new Vector2(240f, 54f));
            SetObjectReference(hud, "statusText", status);
            SetObjectReference(hud, "disconnectButton", disconnect);
            CreateEventSystem();
            CreateSceneScope(scene);

            EditorSceneManager.SaveScene(scene, path);
        }

        private static void ConfigureProPixelizerRenderingAssets()
        {
            var atlas = LoadRequiredAsset<Texture2D>(BlockAtlasPath);
            var material = LoadRequiredAsset<Material>(ProPixelizerBlockMaterialPath);
            var materialSet = AssetDatabase.LoadAssetAtPath<ChunkMaterialSet>(
                ProPixelizerChunkMaterialSetPath);
            if (materialSet == null)
            {
                materialSet = ScriptableObject.CreateInstance<ChunkMaterialSet>();
                AssetDatabase.CreateAsset(materialSet, ProPixelizerChunkMaterialSetPath);
            }

            var serialized = new SerializedObject(materialSet);
            serialized.FindProperty("atlas").objectReferenceValue = atlas;
            serialized.FindProperty("opaque").objectReferenceValue = material;
            serialized.FindProperty("cutout").objectReferenceValue = material;
            serialized.FindProperty("transparent").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(materialSet);
        }

        private static ChunkMaterialSet ConfigureGameSceneRendering(Camera camera)
        {
            ConfigurePixelStableGameCamera(camera);
            CreateMinecraftMapPostProcessing();
            return LoadRequiredAsset<ChunkMaterialSet>(ProPixelizerChunkMaterialSetPath);
        }

        private static void ConfigureGameSceneLighting(Light light)
        {
            if (light == null)
                throw new ArgumentNullException(nameof(light));

            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1f;
            light.shadowBias = 0.05f;
            light.shadowNormalBias = 0.4f;
            light.shadowNearPlane = 0.2f;
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        }

        private static void ConfigurePixelStableGameCamera(Camera camera)
        {
            ConfigureMinecraftMapCamera(camera);
            camera.orthographicSize = PixelStableGameOrthographicSize;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            var pixelCamera = GetOrAdd<ProPixelizerCamera>(camera.gameObject);
            pixelCamera.SubCamera = null;
            pixelCamera.SubcameraExcludedLayers = (1 << 5) | (1 << 1);
            pixelCamera.FixedTargetResolution = 320;
            pixelCamera.ClampCameraOrthoSize = false;
            pixelCamera.Mode = ProPixelizerCamera.PixelSizeMode.WorldSpacePixelSize;
            pixelCamera.PixelSize = ProPixelizerWorldSpacePixelSize;
            pixelCamera.overrideUsePixelExpansion = false;
            pixelCamera.UsePixelExpansion = true;
            pixelCamera.PixelizationFilter = PixelizationFilter.OnlyProPixelizer;
            pixelCamera.UsePixelArtUpscalingFilter = true;
            pixelCamera.TargetScale = 1f;
            pixelCamera.FullscreenColorGradingLUT = null;
            pixelCamera.EnableProPixelizer = true;
        }

        private static void ConfigureProPixelizerVisual(Transform visual)
        {
            if (visual == null)
                throw new InvalidOperationException("Player prefab is missing its Visual root.");

            var material = LoadRequiredAsset<Material>(ProPixelizerBlockMaterialPath);
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("Player Visual has no renderers to configure.");

            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                    materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static Camera CreateMinecraftMapCamera(string name)
        {
            var camera = new GameObject(name).AddComponent<Camera>();
            ConfigureMinecraftMapCamera(camera);
            camera.tag = "MainCamera";
            return camera;
        }

        private static void ConfigureMinecraftMapCamera(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.43f, 0.69f, 0.92f, 1f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 160f;
            camera.fieldOfView = 40f;
            camera.orthographic = true;
            camera.orthographicSize = MinecraftMapOrthographicSize;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = true;

            var cameraData = GetOrAdd<UniversalAdditionalCameraData>(camera.gameObject);
            cameraData.renderShadows = true;
            cameraData.volumeLayerMask = 1;
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.None;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.stopNaN = false;
            cameraData.dithering = false;
            cameraData.allowXRRendering = true;
            cameraData.allowHDROutput = true;
        }

        private static void CreateMinecraftMapPostProcessing()
        {
            var volume = new GameObject("Minecraft Map Post Processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.blendDistance = 0f;
            volume.weight = 1f;
            volume.sharedProfile = LoadRequiredAsset<VolumeProfile>(
                MinecraftMapVolumeProfilePath);
        }

        private static void CreateSceneScope(Scene scene)
        {
            var scope = new GameObject("SceneLifetimeScope").AddComponent<SceneLifetimeScope>();
            scope.ConfigureSceneRoots(scene.GetRootGameObjects());
        }

        private static void ConfigureBuildSettings()
        {
            var required = new[] { TitlePath, OutgamePath, TestPath, IngamePath };
            var existing = EditorBuildSettings.scenes
                .Where(entry => !required.Contains(entry.path, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var result = new List<EditorBuildSettingsScene>
            {
                new(TitlePath, true),
                new(OutgamePath, true),
                new(TestPath, true),
                new(IngamePath, true)
            };
            result.AddRange(existing);
            EditorBuildSettings.scenes = result.ToArray();
        }

        private static GameObject CreateCanvasWithPanel(string canvasName, Vector2 panelSize)
        {
            var canvas = CreateCanvas(canvasName);
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.VerticalLayoutGroup), typeof(UnityEngine.UI.ContentSizeFitter));
            panel.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = panelSize;
            var image = panel.GetComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.045f, 0.06f, 0.085f, 0.96f);
            var layout = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new RectOffset(42, 42, 36, 36);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            panel.GetComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            return panel;
        }

        private static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static UnityEngine.UI.Text CreateText(GameObject parent, string value, int size, TextAnchor alignment, float height)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(UnityEngine.UI.Text), typeof(UnityEngine.UI.LayoutElement));
            go.transform.SetParent(parent.transform, false);
            var text = go.GetComponent<UnityEngine.UI.Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            go.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = height;
            return text;
        }

        private static UnityEngine.UI.Button CreateButton(GameObject parent, string label, float height, float width = -1f)
        {
            var go = new GameObject(label + " Button", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.LayoutElement));
            go.transform.SetParent(parent.transform, false);
            go.GetComponent<UnityEngine.UI.Image>().color = new Color(0.12f, 0.42f, 0.7f, 1f);
            var element = go.GetComponent<UnityEngine.UI.LayoutElement>();
            element.preferredHeight = height;
            if (width > 0f) element.preferredWidth = width;
            var text = CreateText(go, label, 22, TextAnchor.MiddleCenter, height);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return go.GetComponent<UnityEngine.UI.Button>();
        }

        private static GameObject CreateHorizontalRow(GameObject parent, string name, float height)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup), typeof(UnityEngine.UI.LayoutElement));
            row.transform.SetParent(parent.transform, false);
            var layout = row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
            row.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = height;
            return row;
        }

        private static UnityEngine.UI.InputField CreateInput(GameObject parent, string initialValue, string placeholderValue, float width)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.InputField), typeof(UnityEngine.UI.LayoutElement));
            go.transform.SetParent(parent.transform, false);
            go.GetComponent<UnityEngine.UI.Image>().color = new Color(0.12f, 0.14f, 0.18f, 1f);
            var element = go.GetComponent<UnityEngine.UI.LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = 60f;

            var placeholder = CreateInputText(go.transform, "Placeholder", placeholderValue, new Color(1f, 1f, 1f, 0.35f));
            var text = CreateInputText(go.transform, "Text", initialValue, Color.white);
            var input = go.GetComponent<UnityEngine.UI.InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = initialValue;
            return input;
        }

        private static UnityEngine.UI.Text CreateInputText(Transform parent, string name, string value, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(14f, 6f);
            rect.offsetMax = new Vector2(-14f, -6f);
            var text = go.GetComponent<UnityEngine.UI.Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = color;
            text.text = value;
            text.supportRichText = false;
            return text;
        }

        private static UnityEngine.UI.Text CreateAnchoredText(Transform parent, string value, Vector2 offset, Vector2 size)
        {
            var text = CreateText(parent.gameObject, value, 20, TextAnchor.UpperLeft, size.y);
            text.gameObject.name = "Status";
            var rect = (RectTransform)text.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return text;
        }

        private static UnityEngine.UI.Button CreateAnchoredButton(Transform parent, string label, Vector2 offset, Vector2 size)
        {
            var button = CreateButton(parent.gameObject, label, size.y, size.x);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return button;
        }

        private static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component =>
            gameObject.GetComponent<T>() ?? gameObject.AddComponent<T>();

        private static T LoadRequiredAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null
                ? asset
                : throw new InvalidOperationException($"Required asset was not found: {path}");
        }

        private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName) ?? throw new MissingFieldException(target.GetType().FullName, propertyName);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
