using System.Collections.Generic;
using System.Linq;
using MF2.Demo;
using ProPixelizer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MF2.EditorTools
{
    public static class ProPixelizerDemoBuilder
    {
        private const string DemoRoot = "Assets/ProPixelizerDemo";
        private const string MaterialRoot = DemoRoot + "/Materials";
        private const string ScenePath = "Assets/Scenes/ProPixelizerDemo.unity";
        private const string TemplateMaterialPath =
            "Assets/External/ProPixelizer/ExampleAssets/Edges and Outlines/Test_Outline.mat";

        private static byte nextOutlineId;

        [MenuItem("Tools/MF2/Create ProPixelizer Demo")]
        public static void Build()
        {
            EnsureFolder(DemoRoot);
            EnsureFolder(MaterialRoot);

            var template = AssetDatabase.LoadAssetAtPath<Material>(TemplateMaterialPath);
            if (template == null)
            {
                throw new System.InvalidOperationException(
                    $"ProPixelizer template material was not found at {TemplateMaterialPath}.");
            }

            var ground = CreateMaterial(template, "Ground", new Color(0.10f, 0.29f, 0.31f), 4f);
            var stone = CreateMaterial(template, "Stone", new Color(0.30f, 0.39f, 0.52f), 4f);
            var body = CreateMaterial(template, "HeroBody", new Color(0.91f, 0.30f, 0.28f), 3f);
            var accent = CreateMaterial(template, "HeroAccent", new Color(1.00f, 0.73f, 0.25f), 2f);
            var foliage = CreateMaterial(template, "Foliage", new Color(0.27f, 0.65f, 0.38f), 3f);
            var crystal = CreateMaterial(template, "Crystal", new Color(0.35f, 0.86f, 0.95f), 2f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "ProPixelizerDemo";
            nextOutlineId = 1;

            var root = new GameObject("ProPixelizer Demo");
            BuildCamera(root.transform);
            BuildLighting(root.transform);
            BuildEnvironment(root.transform, ground, stone, foliage);
            BuildHero(root.transform, body, accent, stone);
            BuildCrystal(root.transform, crystal, accent);

            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.18f, 0.22f, 0.32f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.LookAt(new Vector3(0f, 1.25f, 0f),
                    Quaternion.Euler(28f, 315f, 0f), 9f);
                SceneView.lastActiveSceneView.Repaint();
            }

            Debug.Log($"Created ProPixelizer demo scene at {ScenePath}");
        }

        private static void BuildCamera(Transform parent)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(8.5f, 7.5f, -9.5f);
            cameraObject.transform.LookAt(new Vector3(0f, 1.2f, 0f));

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.6f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.085f);
            camera.allowHDR = false;
            camera.allowMSAA = false;

            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.requiresDepthTexture = true;
            cameraData.renderShadows = true;

            var pixelCamera = cameraObject.AddComponent<ProPixelizerCamera>();
            pixelCamera.Mode = ProPixelizerCamera.PixelSizeMode.FixedTargetResolution;
            pixelCamera.FixedTargetResolution = 180;
            pixelCamera.PixelizationFilter = PixelizationFilter.OnlyProPixelizer;
            pixelCamera.UsePixelExpansion = true;
            pixelCamera.UsePixelArtUpscalingFilter = true;
            pixelCamera.EnableProPixelizer = true;

            cameraObject.AddComponent<AudioListener>();
        }

        private static void BuildLighting(Transform parent)
        {
            var sunObject = new GameObject("Key Light");
            sunObject.transform.SetParent(parent, false);
            sunObject.transform.rotation = Quaternion.Euler(48f, -38f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.82f, 0.68f);
            sun.intensity = 1.45f;
            sun.shadows = LightShadows.Soft;

            var fillObject = new GameObject("Cool Fill Light");
            fillObject.transform.SetParent(parent, false);
            fillObject.transform.position = new Vector3(-3.5f, 4.5f, -2f);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(0.32f, 0.56f, 1f);
            fill.intensity = 7f;
            fill.range = 10f;
            fill.shadows = LightShadows.None;
        }

        private static void BuildEnvironment(
            Transform parent,
            Material ground,
            Material stone,
            Material foliage)
        {
            var environment = new GameObject("Environment").transform;
            environment.SetParent(parent, false);

            CreateBlock("Ground", environment, new Vector3(0f, -0.55f, 0f),
                new Vector3(12f, 0.8f, 8f), ground);
            CreateBlock("Raised Platform", environment, new Vector3(0f, 0f, 0f),
                new Vector3(5.8f, 0.35f, 4.4f), stone);

            CreateBlock("Step Left", environment, new Vector3(-1.4f, 0.25f, -2.45f),
                new Vector3(1.2f, 0.35f, 0.75f), stone);
            CreateBlock("Step Right", environment, new Vector3(1.4f, 0.25f, -2.45f),
                new Vector3(1.2f, 0.35f, 0.75f), stone);

            CreatePillar(environment, new Vector3(-4.1f, 0f, 2.25f), stone, foliage);
            CreatePillar(environment, new Vector3(4.1f, 0f, 2.25f), stone, foliage);
            CreatePillar(environment, new Vector3(-4.4f, 0f, -2.5f), stone, foliage);
            CreatePillar(environment, new Vector3(4.4f, 0f, -2.5f), stone, foliage);

            for (var i = -2; i <= 2; i++)
            {
                CreateBlock($"Back Tile {i + 3}", environment,
                    new Vector3(i * 1.5f, 0.2f, 3.1f), new Vector3(1.15f, 0.45f, 0.8f),
                    i % 2 == 0 ? foliage : stone);
            }
        }

        private static void BuildHero(
            Transform parent,
            Material body,
            Material accent,
            Material stone)
        {
            var hero = new GameObject("Pixel Guardian").transform;
            hero.SetParent(parent, false);
            hero.localPosition = new Vector3(-1.1f, 0.35f, -0.1f);

            CreateBlock("Body", hero, new Vector3(0f, 1.15f, 0f),
                new Vector3(1.45f, 1.35f, 0.9f), body);
            CreateBlock("Head", hero, new Vector3(0f, 2.25f, 0f),
                new Vector3(1.25f, 0.85f, 0.9f), body);
            CreateBlock("Visor", hero, new Vector3(0f, 2.25f, -0.48f),
                new Vector3(0.82f, 0.24f, 0.10f), accent, castShadows: false);
            CreateBlock("Left Arm", hero, new Vector3(-0.95f, 1.2f, 0f),
                new Vector3(0.35f, 1.2f, 0.45f), body);
            CreateBlock("Right Arm", hero, new Vector3(0.95f, 1.2f, 0f),
                new Vector3(0.35f, 1.2f, 0.45f), body);
            CreateBlock("Left Foot", hero, new Vector3(-0.42f, 0.28f, -0.05f),
                new Vector3(0.52f, 0.55f, 0.72f), stone);
            CreateBlock("Right Foot", hero, new Vector3(0.42f, 0.28f, -0.05f),
                new Vector3(0.52f, 0.55f, 0.72f), stone);
            CreateBlock("Chest Core", hero, new Vector3(0f, 1.2f, -0.48f),
                new Vector3(0.38f, 0.38f, 0.10f), accent, castShadows: false,
                localEuler: new Vector3(0f, 0f, 45f));

            var snap = hero.gameObject.AddComponent<ObjectRenderSnapable>();
            snap.SnapPosition = true;
            snap.SnapEulerAngles = true;
            snap.angleResolution = 15f;
            snap.ConfigureChildren = true;
        }

        private static void BuildCrystal(Transform parent, Material crystal, Material accent)
        {
            var spinner = new GameObject("Animated Crystal").transform;
            spinner.SetParent(parent, false);
            spinner.localPosition = new Vector3(1.8f, 1.2f, 0.3f);
            spinner.gameObject.AddComponent<ProPixelizerDemoSpinner>();

            CreateBlock("Crystal Core", spinner, Vector3.zero, new Vector3(0.8f, 1.5f, 0.8f),
                crystal, localEuler: new Vector3(0f, 45f, 45f));
            CreateBlock("Crystal Ring North", spinner, new Vector3(0f, 0f, 0.9f),
                new Vector3(0.28f, 0.28f, 0.28f), accent, castShadows: false);
            CreateBlock("Crystal Ring South", spinner, new Vector3(0f, 0f, -0.9f),
                new Vector3(0.28f, 0.28f, 0.28f), accent, castShadows: false);
            CreateBlock("Crystal Ring East", spinner, new Vector3(0.9f, 0f, 0f),
                new Vector3(0.28f, 0.28f, 0.28f), accent, castShadows: false);
            CreateBlock("Crystal Ring West", spinner, new Vector3(-0.9f, 0f, 0f),
                new Vector3(0.28f, 0.28f, 0.28f), accent, castShadows: false);

            var snap = spinner.gameObject.AddComponent<ObjectRenderSnapable>();
            snap.SnapPosition = true;
            snap.SnapEulerAngles = true;
            snap.angleResolution = 15f;
            snap.ConfigureChildren = true;
        }

        private static void CreatePillar(
            Transform parent,
            Vector3 position,
            Material stone,
            Material foliage)
        {
            var pillar = new GameObject("Pixel Tree").transform;
            pillar.SetParent(parent, false);
            pillar.localPosition = position;
            CreateBlock("Trunk", pillar, new Vector3(0f, 0.85f, 0f),
                new Vector3(0.45f, 1.7f, 0.45f), stone);
            CreateBlock("Crown Low", pillar, new Vector3(0f, 1.85f, 0f),
                new Vector3(1.3f, 0.7f, 1.3f), foliage);
            CreateBlock("Crown High", pillar, new Vector3(0f, 2.45f, 0f),
                new Vector3(0.8f, 0.65f, 0.8f), foliage);
        }

        private static GameObject CreateBlock(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool castShadows = true,
            Vector3? localEuler = null)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localPosition;
            block.transform.localRotation = Quaternion.Euler(localEuler ?? Vector3.zero);
            block.transform.localScale = localScale;

            var collider = block.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            var renderer = block.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;

            var outline = block.AddComponent<OutlineControl>();
            outline.ID = nextOutlineId++;
            outline.UseRandomUID = false;
            outline.UseRootUID = false;
            outline.UseRootColor = false;
            outline.OverrideColor = true;
            outline.Color = new Color(0.015f, 0.02f, 0.04f, 1f);
            return block;
        }

        private static Material CreateMaterial(
            Material template,
            string name,
            Color baseColor,
            float pixelSize)
        {
            var path = $"{MaterialRoot}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(template) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                EditorUtility.CopySerialized(template, material);
                material.name = name;
            }

            SetColorIfPresent(material, "_BaseColor", baseColor);
            SetColorIfPresent(material, "_Color", baseColor);
            SetColorIfPresent(material, "_OutlineColor", new Color(0.015f, 0.02f, 0.04f, 1f));
            SetColorIfPresent(material, "_AmbientLight", baseColor * 0.22f);
            SetColorIfPresent(material, "_ShadowTintColor", new Color(0.02f, 0.03f, 0.08f, 0.45f));
            if (material.HasProperty("_PixelSize")) material.SetFloat("_PixelSize", pixelSize);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetColorIfPresent(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            var name = path.Substring(path.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(scene => scene.path != ScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }
    }
}
