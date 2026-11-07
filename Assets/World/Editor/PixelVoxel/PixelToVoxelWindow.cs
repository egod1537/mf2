using System;
using System.Collections.Generic;
using MF2.EditorUI;
using MF2.World.Core;
using MF2.World.Registry;
using MF2.World.Storage;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MF2.World.Editor.Tools
{
    public enum PixelVoxelPlane : byte
    {
        XY,
        XZ,
        ZY
    }

    public readonly struct PixelVoxelPlacement
    {
        public PixelVoxelPlacement(int3 position, Block block, Color32 sourceColor)
        {
            Position = position;
            Block = block;
            SourceColor = sourceColor;
        }

        public int3 Position { get; }
        public Block Block { get; }
        public Color32 SourceColor { get; }
    }

    public static class PixelVoxelConverter
    {
        public const int MaximumPlacements = 65536;
        private static readonly PaletteEntry[] Palette =
        {
            new PaletteEntry("minecraft:grass_block", new Color32(92, 154, 68, 255)),
            new PaletteEntry("minecraft:dirt", new Color32(126, 88, 60, 255)),
            new PaletteEntry("minecraft:stone", new Color32(125, 127, 132, 255)),
            new PaletteEntry("minecraft:water", new Color32(45, 102, 205, 255))
        };

        public static bool TryConvert(
            UnityEngine.Object source,
            Vector3Int origin,
            PixelVoxelPlane plane,
            int pixelScale,
            int depth,
            byte alphaThreshold,
            out PixelVoxelPlacement[] placements,
            out string error)
        {
            placements = Array.Empty<PixelVoxelPlacement>();
            error = null;
            if (!TryResolveTexture(source, out var texture, out var pixelRect))
            {
                error = "Assign a readable Texture2D or Sprite.";
                return false;
            }
            if (pixelScale < 1 || pixelScale > 8 || depth < 1 || depth > 32)
            {
                error = "Scale must be 1..8 and depth must be 1..32.";
                return false;
            }

            var width = Mathf.RoundToInt(pixelRect.width);
            var height = Mathf.RoundToInt(pixelRect.height);
            var estimate = (long)width * height * pixelScale * pixelScale * depth;
            if (estimate > MaximumPlacements)
            {
                error = $"Conversion may exceed the {MaximumPlacements:N0} voxel safety budget.";
                return false;
            }

            var result = new List<PixelVoxelPlacement>((int)estimate);
            try
            {
                var startX = Mathf.RoundToInt(pixelRect.x);
                var startY = Mathf.RoundToInt(pixelRect.y);
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var color = texture.GetPixel(startX + x, startY + y);
                    var color32 = (Color32)color;
                    if (color32.a < alphaThreshold)
                        continue;
                    var block = new Block(FindNearestBlock(color32));
                    for (var sy = 0; sy < pixelScale; sy++)
                    for (var sx = 0; sx < pixelScale; sx++)
                    for (var layer = 0; layer < depth; layer++)
                    {
                        result.Add(new PixelVoxelPlacement(
                            ToPosition(origin, plane, x * pixelScale + sx, y * pixelScale + sy, layer),
                            block,
                            color32));
                    }
                }
            }
            catch (UnityException)
            {
                error = "The source texture is not readable. Enable Read/Write in its importer.";
                return false;
            }

            placements = result.ToArray();
            return true;
        }

        private static int3 ToPosition(
            Vector3Int origin,
            PixelVoxelPlane plane,
            int horizontal,
            int vertical,
            int depth) => plane switch
        {
            PixelVoxelPlane.XY => new int3(origin.x + horizontal, origin.y + vertical, origin.z + depth),
            PixelVoxelPlane.XZ => new int3(origin.x + horizontal, origin.y + depth, origin.z + vertical),
            PixelVoxelPlane.ZY => new int3(origin.x + depth, origin.y + vertical, origin.z + horizontal),
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };

        private static uint FindNearestBlock(Color32 source)
        {
            var bestDistance = long.MaxValue;
            var best = Palette[0].Id;
            for (var i = 0; i < Palette.Length; i++)
            {
                var color = Palette[i].Color;
                var red = source.r - color.r;
                var green = source.g - color.g;
                var blue = source.b - color.b;
                var distance = (long)red * red + (long)green * green + (long)blue * blue;
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                best = Palette[i].Id;
            }
            return best;
        }

        private static bool TryResolveTexture(
            UnityEngine.Object source,
            out Texture2D texture,
            out Rect rect)
        {
            if (source is Sprite sprite)
            {
                texture = sprite.texture;
                rect = sprite.textureRect;
                return texture != null;
            }
            texture = source as Texture2D;
            rect = texture == null ? default : new Rect(0, 0, texture.width, texture.height);
            return texture != null;
        }

        private readonly struct PaletteEntry
        {
            public PaletteEntry(string canonicalName, Color32 color)
            {
                Id = StableBlockId.Compute(canonicalName);
                Color = color;
            }
            public uint Id { get; }
            public Color32 Color { get; }
        }
    }

    public sealed class PixelToVoxelWindow : EditorWindow
    {
        private UnityEngine.Object source;
        private WorldHost host;
        private Vector3Int origin;
        private PixelVoxelPlane plane = PixelVoxelPlane.XY;
        private int scale = 1;
        private int depth = 1;
        private int alphaThreshold = 8;
        private PixelVoxelPlacement[] preview = Array.Empty<PixelVoxelPlacement>();
        private string status = "Choose a Texture2D/Sprite and an authoring WorldHost.";

        [MenuItem("Tools/World/Pixel Art to Voxels")]
        private static void Open() => GetWindow<PixelToVoxelWindow>("Pixel to Voxels");

        private void OnEnable()
        {
            SceneView.duringSceneGui += DrawPreview;
            Undo.undoRedoPerformed += RefreshAfterUndoRedo;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawPreview;
            Undo.undoRedoPerformed -= RefreshAfterUndoRedo;
        }

        private void OnGUI()
        {
            using (Gui.Vertical().Padding(12).Spacing(8f).Begin())
            {
                Gui.Label("Pixel Art → World Voxels").Bold().FontSize(18).Draw();
                Gui.Label("Preview is transient. Apply writes through WorldHost.Enqueue/Flush.")
                    .Color(new Color(0.65f, 0.72f, 0.82f)).Draw();

                using (Gui.Vertical().Begin())
                {
                    source = EditorGUILayout.ObjectField(
                        "Source", source, typeof(UnityEngine.Object), false);
                    host = (WorldHost)EditorGUILayout.ObjectField(
                        "World Host", host, typeof(WorldHost), true);
                    origin = EditorGUILayout.Vector3IntField("Origin", origin);
                    plane = (PixelVoxelPlane)EditorGUILayout.EnumPopup("Plane", plane);
                    scale = EditorGUILayout.IntSlider("Pixel Scale", scale, 1, 8);
                    depth = EditorGUILayout.IntSlider("Depth", depth, 1, 32);
                    alphaThreshold = EditorGUILayout.IntSlider(
                        "Alpha Threshold", alphaThreshold, 0, 255);
                }

                using (Gui.Horizontal().Spacing(8f).Begin())
                {
                    if (Gui.Button("Preview").Width(100f).Draw())
                        BuildPreview();
                    if (Gui.Button("Clear").Width(80f).Draw())
                    {
                        preview = Array.Empty<PixelVoxelPlacement>();
                        status = "Preview cleared.";
                        SceneView.RepaintAll();
                    }
                    if (Gui.Button("Apply").Bold().Width(100f)
                            .Enabled(preview.Length > 0 && host != null).Draw())
                        ApplyPreview();
                }

                if (Gui.Button("Save World Regions…")
                        .Enabled(host != null && !EditorApplication.isPlaying).Draw())
                    SaveWorld();

                Gui.Label($"{status}\nPreview voxels: {preview.Length:N0}").Draw();
            }
        }

        private void BuildPreview()
        {
            if (EditorApplication.isPlaying)
            {
                status = "Authoring is disabled in Play Mode.";
                return;
            }
            if (!PixelVoxelConverter.TryConvert(
                    source, origin, plane, scale, depth, (byte)alphaThreshold,
                    out preview, out var error))
            {
                status = error;
                return;
            }
            status = preview.Length == 0 ? "No opaque pixels were found." : "Preview ready.";
            SceneView.RepaintAll();
        }

        private void ApplyPreview()
        {
            if (host == null || preview.Length == 0 || EditorApplication.isPlaying)
                return;
            if (!host.HasEditorAssets)
            {
                status = "WorldHost is missing registry/render/material assets.";
                return;
            }

            Undo.RecordObject(host, "Apply Pixel Art Voxels");
            for (var i = 0; i < preview.Length; i++)
                host.Enqueue(WorldCommand.SetBlock(preview[i].Position, preview[i].Block));
            var result = host.Flush();
            EditorUtility.SetDirty(host);
            if (host.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
            status = $"Applied {result.ChangedCells:N0} changed voxels.";
        }

        private void SaveWorld()
        {
            var path = EditorUtility.OpenFolderPanel("Save World Regions", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(path))
                return;
            var result = host.SaveWorld(path);
            status = $"Saved {result.SnapshotsWritten:N0} chunk snapshots to {path}.";
        }

        private void DrawPreview(SceneView _)
        {
            if (preview == null || preview.Length == 0 || Event.current.type != EventType.Repaint)
                return;
            for (var i = 0; i < preview.Length; i++)
            {
                var placement = preview[i];
                var color = (Color)placement.SourceColor;
                color.a = 0.4f;
                Handles.color = color;
                var center = new Vector3(
                    placement.Position.x + 0.5f,
                    placement.Position.y + 0.5f,
                    placement.Position.z + 0.5f);
                Handles.DrawWireCube(center, Vector3.one * 0.94f);
            }
        }

        private void RefreshAfterUndoRedo()
        {
            if (host != null)
            {
                host.DisposeWorld();
                host.GetBlockOrAir(default);
            }
            SceneView.RepaintAll();
            Repaint();
        }
    }
}
