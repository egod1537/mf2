using System;
using System.Collections.Generic;
using System.Linq;
using MF2.World.Core;
using MF2.World.Editor.Palette;
using MF2.World.Editor.Registry;
using MF2.World.Editor.Tools;
using MF2.World.Registry;
using MF2.World.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MF2.World.Tests
{
    public sealed class EditorToolTests
    {
        [Test]
        public void WorldHost_DisposesAndRehydratesManagedSceneRecords()
        {
            var previewScene = EditorSceneManager.NewPreviewScene();
            var gameObject = new GameObject("World Host Test");
            SceneManager.MoveGameObjectToScene(gameObject, previewScene);
            var host = gameObject.AddComponent<WorldHost>();
            try
            {
                host.ConfigureAssets(
                    AssetDatabase.LoadAssetAtPath<BlockRegistryManifest>(
                        BlockRegistryManifestBuilder.ManifestPath),
                    AssetDatabase.LoadAssetAtPath<BlockRenderManifest>(
                        BlockRenderManifestBuilder.RenderManifestPath),
                    AssetDatabase.LoadAssetAtPath<ChunkMaterialSet>(
                        BlockRenderManifestBuilder.MaterialSetPath));

                var stone = StableBlockId.Compute("minecraft:stone");
                var oak = StableBlockId.Compute("minecraft:oak_log");
                host.Enqueue(WorldCommand.SetBlock(new int3(-1, 2, -17), new Block(stone, 3)));
                host.Enqueue(WorldCommand.SetBlock(new int3(16, 0, 0), new Block(oak, 9)));
                host.Flush();

                Assert.That(host.SerializedBlockCount, Is.EqualTo(2));
                Assert.That(host.GetBlockOrAir(new int3(-1, 2, -17)),
                    Is.EqualTo(new Block(stone, 3)));

                gameObject.SetActive(false);
                Assert.That(host.IsInitialized, Is.False);
                gameObject.SetActive(true);
                Assert.That(host.IsInitialized, Is.True);
                Assert.That(host.GetBlockOrAir(new int3(-1, 2, -17)),
                    Is.EqualTo(new Block(stone, 3)));
                Assert.That(host.GetBlockOrAir(new int3(16, 0, 0)),
                    Is.EqualTo(new Block(oak, 9)));

                host.DisposeWorld();
                Assert.That(host.IsInitialized, Is.False);
                Assert.That(gameObject.transform.childCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        [Test]
        public void Raycast_HitsVoxelFaceAndPaintTargetsAdjacentCell()
        {
            WithHost(host =>
            {
                var registry = AssetDatabase.LoadAssetAtPath<BlockRegistryManifest>(
                    BlockRegistryManifestBuilder.ManifestPath);
                BlockToolState.ResolveSelection(registry);
                BlockToolState.Mode = BlockEditMode.Paint;
                host.Enqueue(WorldCommand.SetBlock(int3.zero, BlockToolState.SelectedBlock));
                host.Flush();

                var ray = new Ray(new Vector3(0f, 0f, -5f), Vector3.forward);
                Assert.That(
                    VoxelEditingGeometry.TryRaycast(host, ray, 32f, out var hit),
                    Is.True);
                Assert.That(hit.Position, Is.EqualTo(int3.zero));
                Assert.That(hit.Normal, Is.EqualTo(new int3(0, 0, -1)));

                Assert.That(
                    BlockPaintTool.TryResolveTarget(
                        host,
                        ray,
                        BlockEditMode.Paint,
                        out var paintTarget,
                        out _),
                    Is.True);
                Assert.That(paintTarget, Is.EqualTo(new int3(0, 0, -1)));

                Assert.That(
                    BlockPaintTool.TryResolveTarget(
                        host,
                        ray,
                        BlockEditMode.Erase,
                        out var eraseTarget,
                        out _),
                    Is.True);
                Assert.That(eraseTarget, Is.EqualTo(int3.zero));
            });
        }

        [Test]
        public void WorkPlanes_SnapSignedCoordinatesOnAllAxes()
        {
            WithHost(host =>
            {
                Assert.That(VoxelEditingGeometry.TryIntersectWorkPlane(
                    host,
                    new Ray(new Vector3(-2.6f, 10f, 3.6f), Vector3.down),
                    WorkPlaneAxis.XZ,
                    0,
                    32f,
                    out var xz), Is.True);
                Assert.That(xz, Is.EqualTo(new int3(-3, 0, 4)));

                Assert.That(VoxelEditingGeometry.TryIntersectWorkPlane(
                    host,
                    new Ray(new Vector3(-2.6f, 3.6f, -10f), Vector3.forward),
                    WorkPlaneAxis.XY,
                    -2,
                    32f,
                    out var xy), Is.True);
                Assert.That(xy, Is.EqualTo(new int3(-3, 4, -2)));

                Assert.That(VoxelEditingGeometry.TryIntersectWorkPlane(
                    host,
                    new Ray(new Vector3(10f, -2.6f, 3.6f), Vector3.left),
                    WorkPlaneAxis.YZ,
                    -1,
                    32f,
                    out var yz), Is.True);
                Assert.That(yz, Is.EqualTo(new int3(-1, -3, 4)));
            });
        }

        [Test]
        public void FastStrokeRasterization_HasNoGridGaps()
        {
            var cells = new List<int3>();
            Assert.That(
                GridStrokeRasterizer.TryFill(
                    new int3(-10, -4, -7),
                    new int3(13, 5, 11),
                    cells),
                Is.True);
            Assert.That(cells[0], Is.EqualTo(new int3(-10, -4, -7)));
            Assert.That(cells[cells.Count - 1], Is.EqualTo(new int3(13, 5, 11)));
            Assert.That(cells.Distinct().Count(), Is.EqualTo(cells.Count));

            for (var i = 1; i < cells.Count; i++)
            {
                var delta = math.abs(cells[i] - cells[i - 1]);
                Assert.That(delta.x, Is.LessThanOrEqualTo(1));
                Assert.That(delta.y, Is.LessThanOrEqualTo(1));
                Assert.That(delta.z, Is.LessThanOrEqualTo(1));
            }
        }

        [Test]
        public void RasterizedStroke_AppliesAsOnePaintAndOneEraseBatch()
        {
            WithHost(host =>
            {
                var cells = new List<int3>();
                GridStrokeRasterizer.TryFill(
                    new int3(-4, 0, -3),
                    new int3(5, 2, 4),
                    cells);
                var stone = new Block(StableBlockId.Compute("minecraft:stone"), 0);
                for (var i = 0; i < cells.Count; i++)
                    host.Enqueue(WorldCommand.SetBlock(cells[i], stone));

                Assert.That(host.GetBlockOrAir(cells[cells.Count - 1]).IsAir, Is.True);
                var paint = host.Flush();
                Assert.That(paint.CommandsApplied, Is.EqualTo(cells.Count));
                for (var i = 0; i < cells.Count; i++)
                    Assert.That(host.GetBlockOrAir(cells[i]), Is.EqualTo(stone));

                for (var i = 0; i < cells.Count; i++)
                    host.Enqueue(WorldCommand.RemoveBlock(cells[i]));
                Assert.That(host.GetBlockOrAir(cells[0]), Is.EqualTo(stone));
                var erase = host.Flush();
                Assert.That(erase.CommandsApplied, Is.EqualTo(cells.Count));
                for (var i = 0; i < cells.Count; i++)
                    Assert.That(host.GetBlockOrAir(cells[i]).IsAir, Is.True);
            });
        }

        [Test]
        public void EmptyWorkPlaneStroke_DeduplicatesRepeatedDragSamplesIntoOneCommand()
        {
            WithHost(host =>
            {
                var registry = host.BlockRegistryManifest;
                var stone = registry.Entries.Single(entry =>
                    entry.CanonicalName == "minecraft:stone");
                var stoneBlock = new Block(StableBlockId.Compute(stone.CanonicalName));
                BlockToolState.Select(stone);
                BlockToolState.Mode = BlockEditMode.Paint;
                BlockToolState.PlaneAxis = WorkPlaneAxis.XZ;
                BlockToolState.PlaneOffset = 0;

                var ray = new Ray(new Vector3(-2.6f, 10f, 3.6f), Vector3.down);
                Assert.That(BlockPaintTool.TryResolveTarget(
                    host,
                    ray,
                    BlockEditMode.Paint,
                    out var target,
                    out _), Is.True);

                var tool = ScriptableObject.CreateInstance<BlockPaintTool>();
                try
                {
                    tool.BeginStroke(host, target, 0, captureMouse: false);
                    tool.AppendStrokeTo(target);
                    tool.AppendStrokeTo(target);
                    Assert.That(host.PendingCommandCount, Is.EqualTo(1));

                    tool.OnWillBeDeactivated();
                    Assert.That(host.GetBlockOrAir(target), Is.EqualTo(stoneBlock));
                }
                finally
                {
                    tool.OnWillBeDeactivated();
                    UnityEngine.Object.DestroyImmediate(tool);
                }
            });
        }

        [Test]
        public void NavigationEvents_AreNeverClaimedAsPaintInput()
        {
            Assert.That(BlockPaintTool.IsNavigationEvent(new Event
            {
                type = EventType.MouseDrag,
                button = 0,
                alt = true
            }), Is.True);
            Assert.That(BlockPaintTool.IsNavigationEvent(new Event
            {
                type = EventType.MouseDrag,
                button = 1
            }), Is.True);
            Assert.That(BlockPaintTool.IsNavigationEvent(new Event
            {
                type = EventType.MouseDrag,
                button = 2
            }), Is.True);
            Assert.That(BlockPaintTool.IsNavigationEvent(new Event
            {
                type = EventType.ScrollWheel
            }), Is.True);
            Assert.That(BlockPaintTool.IsNavigationEvent(new Event
            {
                type = EventType.MouseDown,
                button = 0
            }), Is.False);
        }

        [Test]
        public void Palette_CanSelectDifferentStableBlocksAndFilterCanonicalNames()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<BlockRegistryManifest>(
                BlockRegistryManifestBuilder.ManifestPath);
            var stone = manifest.Entries.Single(entry => entry.CanonicalName == "minecraft:stone");
            var oak = manifest.Entries.Single(entry => entry.CanonicalName == "minecraft:oak_log");

            BlockToolState.Select(stone);
            var first = BlockToolState.SelectedId;
            BlockToolState.Select(oak);
            var second = BlockToolState.SelectedId;
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(BlockToolState.SelectedCanonicalName, Is.EqualTo("minecraft:oak_log"));
            Assert.That(BlockPaletteWindow.MatchesSearch("minecraft:oak_log", "OAK"), Is.True);
            Assert.That(BlockPaletteWindow.MatchesSearch("minecraft:stone", "oak"), Is.False);

            BlockToolState.Select(stone);
        }

        [Test]
        public void Pick_SelectsTheExistingBlocksStableId()
        {
            WithHost(host =>
            {
                var manifest = host.BlockRegistryManifest;
                var stone = manifest.Entries.Single(entry =>
                    entry.CanonicalName == "minecraft:stone");
                var oak = manifest.Entries.Single(entry =>
                    entry.CanonicalName == "minecraft:oak_log");
                host.Enqueue(WorldCommand.SetBlock(
                    int3.zero,
                    new Block(StableBlockId.Compute(stone.CanonicalName))));
                host.Flush();

                BlockToolState.Select(oak);
                BlockPaintTool.Pick(host, int3.zero);
                Assert.That(BlockToolState.SelectedCanonicalName, Is.EqualTo(stone.CanonicalName));
            });
        }

        [Test]
        public void EditorAssembly_HasNoHardDependencyOnOptionalFluentImgui()
        {
            var editorAssembly = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                .Single(assembly => assembly.name == "MF2.World.Editor");
            Assert.That(editorAssembly.assemblyReferences
                    .Select(reference => reference.name),
                Does.Not.Contain("MF2.EditorUI"));
        }

        [Test]
        public void ToolActivation_DoesNotChangeUnitySelectionOrLeakState()
        {
            var selected = new GameObject("Selection Sentinel");
            var tool = ScriptableObject.CreateInstance<BlockPaintTool>();
            try
            {
                Selection.activeGameObject = selected;
                tool.OnActivated();
                Assert.That(Selection.activeGameObject, Is.SameAs(selected));
                tool.OnWillBeDeactivated();
                Assert.That(Selection.activeGameObject, Is.SameAs(selected));
            }
            finally
            {
                tool.OnWillBeDeactivated();
                Selection.activeObject = null;
                UnityEngine.Object.DestroyImmediate(tool);
                UnityEngine.Object.DestroyImmediate(selected);
            }
        }

        private static void WithHost(Action<WorldHost> action)
        {
            var previewScene = EditorSceneManager.NewPreviewScene();
            var gameObject = new GameObject("World Host Geometry Test");
            SceneManager.MoveGameObjectToScene(gameObject, previewScene);
            var host = gameObject.AddComponent<WorldHost>();
            host.ConfigureAssets(
                AssetDatabase.LoadAssetAtPath<BlockRegistryManifest>(
                    BlockRegistryManifestBuilder.ManifestPath),
                AssetDatabase.LoadAssetAtPath<BlockRenderManifest>(
                    BlockRenderManifestBuilder.RenderManifestPath),
                AssetDatabase.LoadAssetAtPath<ChunkMaterialSet>(
                    BlockRenderManifestBuilder.MaterialSetPath));
            try
            {
                action(host);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }
    }
}
