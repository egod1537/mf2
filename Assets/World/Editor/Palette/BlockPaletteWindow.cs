using System;
using System.Collections.Generic;
using MF2.World.Core;
using MF2.World.Editor.Registry;
using MF2.World.Editor.Tools;
using MF2.World.Registry;
using MF2.World.Rendering;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MF2.World.Editor.Palette
{
    public sealed class BlockPaletteWindow : EditorWindow
    {
        private static readonly string[] ModeLabels = { "Paint", "Erase", "Pick" };

        private readonly List<BlockRegistryManifest.Entry> filteredEntries =
            new List<BlockRegistryManifest.Entry>();

        private BlockRegistryManifest registryManifest;
        private BlockRenderManifest renderManifest;
        private ChunkMaterialSet materialSet;
        private Vector2 scrollPosition;
        private string search = string.Empty;
        private GUIStyle titleStyle;

        [MenuItem("Tools/World/Block Palette")]
        public static void ShowWindow()
        {
            var window = GetWindow<BlockPaletteWindow>();
            window.titleContent = new GUIContent("Block Palette");
            window.minSize = new Vector2(280f, 320f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Block Palette");
            minSize = new Vector2(280f, 320f);
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            BlockToolState.Changed += Repaint;
            LoadAssets();
        }

        private void OnDisable()
        {
            BlockToolState.Changed -= Repaint;
        }

        private void OnProjectChange()
        {
            LoadAssets();
            Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Block Palette", titleStyle);
                DrawHostControls();
                GUILayout.Space(8f);
                DrawToolControls();
                GUILayout.Space(8f);
                DrawSearch();
                GUILayout.Space(4f);
                DrawEntries();
            }
        }

        private void DrawHostControls()
        {
            if (WorldHost.TryGetActiveHost(out var host))
            {
                EditorGUILayout.LabelField($"World Host: {host.name}");
                if (!host.HasEditorAssets)
                {
                    EditorGUILayout.HelpBox(
                        "The host is missing registry/render/material references.",
                        MessageType.Warning);
                    using (new EditorGUI.DisabledScope(
                               registryManifest == null ||
                               renderManifest == null ||
                               materialSet == null))
                    {
                        if (GUILayout.Button("Assign Default Assets"))
                            AssignDefaultAssets(host);
                    }
                }
                if (!host.IsGridTransformValid)
                {
                    EditorGUILayout.HelpBox(
                        "World Host rotation must be identity and scale must be (1,1,1).",
                        MessageType.Warning);
                }
                return;
            }

            EditorGUILayout.HelpBox(
                "The active scene has no World Host. Native containers are created only after " +
                "a host is present.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(
                       registryManifest == null || renderManifest == null || materialSet == null))
            {
                if (GUILayout.Button("Create World Host"))
                    CreateWorldHost();
            }
        }

        private void DrawToolControls()
        {
            var mode = (BlockEditMode)GUILayout.Toolbar(
                (int)BlockToolState.Mode,
                ModeLabels);
            BlockToolState.Mode = mode;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (ToolManager.activeToolType == typeof(BlockPaintTool))
                {
                    if (GUILayout.Button("Exit Tool"))
                        ToolManager.RestorePreviousTool();
                }
                else if (GUILayout.Button("Activate Paint Tool"))
                {
                    ToolManager.SetActiveTool<BlockPaintTool>();
                }
            }

            if (BlockToolState.Mode == BlockEditMode.Paint)
            {
                BlockToolState.PlaneAxis = (WorkPlaneAxis)EditorGUILayout.EnumPopup(
                    "Work Plane",
                    BlockToolState.PlaneAxis);
                BlockToolState.PlaneOffset = EditorGUILayout.IntField(
                    "Plane Coordinate",
                    BlockToolState.PlaneOffset);
            }

            var selected = BlockToolState.HasSelection
                ? BlockToolState.SelectedCanonicalName
                : "None";
            EditorGUILayout.LabelField("Selected: " + selected);
        }

        private void DrawSearch()
        {
            EditorGUI.BeginChangeCheck();
            var nextSearch = EditorGUILayout.TextField(
                search,
                EditorStyles.toolbarSearchField);
            if (!EditorGUI.EndChangeCheck())
                return;
            search = nextSearch ?? string.Empty;
            RebuildFilter();
        }

        private void DrawEntries()
        {
            if (registryManifest == null)
            {
                EditorGUILayout.HelpBox(
                    "BlockRegistryManifest.asset was not found.",
                    MessageType.Error);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                for (var i = 0; i < filteredEntries.Count; i++)
                {
                    var entry = filteredEntries[i];
                    var previousBackground = GUI.backgroundColor;
                    if (entry.CanonicalName == BlockToolState.SelectedCanonicalName)
                        GUI.backgroundColor = new Color(0.45f, 0.75f, 1f, 1f);
                    if (GUILayout.Button(entry.CanonicalName, GUILayout.ExpandWidth(true)))
                        BlockToolState.Select(entry);
                    GUI.backgroundColor = previousBackground;
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void LoadAssets()
        {
            registryManifest = AssetDatabase.LoadAssetAtPath<BlockRegistryManifest>(
                BlockRegistryManifestBuilder.ManifestPath);
            renderManifest = AssetDatabase.LoadAssetAtPath<BlockRenderManifest>(
                BlockRenderManifestBuilder.RenderManifestPath);
            materialSet = AssetDatabase.LoadAssetAtPath<ChunkMaterialSet>(
                BlockRenderManifestBuilder.MaterialSetPath);
            BlockToolState.ResolveSelection(registryManifest);
            RebuildFilter();
        }

        private void RebuildFilter()
        {
            filteredEntries.Clear();
            if (registryManifest == null)
                return;

            for (var i = 0; i < registryManifest.Entries.Count; i++)
            {
                var entry = registryManifest.Entries[i];
                if (MatchesSearch(entry.CanonicalName, search))
                    filteredEntries.Add(entry);
            }
        }

        internal static bool MatchesSearch(string canonicalName, string query)
        {
            return string.IsNullOrWhiteSpace(query) ||
                   canonicalName.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CreateWorldHost()
        {
            var gameObject = new GameObject("World Host");
            Undo.RegisterCreatedObjectUndo(gameObject, "Create World Host");
            var host = Undo.AddComponent<WorldHost>(gameObject);
            host.ConfigureAssets(registryManifest, renderManifest, materialSet);
            EditorUtility.SetDirty(host);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Selection.activeGameObject = gameObject;
            ToolManager.SetActiveTool<BlockPaintTool>();
        }

        private void AssignDefaultAssets(WorldHost host)
        {
            Undo.RecordObject(host, "Assign World Host Assets");
            host.ConfigureAssets(registryManifest, renderManifest, materialSet);
            EditorUtility.SetDirty(host);
            EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
        }
    }
}
