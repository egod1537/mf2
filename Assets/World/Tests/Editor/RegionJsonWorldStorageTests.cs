using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MF2.World.Core;
using MF2.World.Editor.Registry;
using MF2.World.Registry;
using MF2.World.Rendering;
using MF2.World.Storage;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MF2.World.Tests
{
    public sealed class RegionJsonWorldStorageTests
    {
        private string temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "MF2.World.Storage.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, true);
        }

        [TestCase(-1, -1, 15)]
        [TestCase(-16, -1, 0)]
        [TestCase(-17, -2, 15)]
        [TestCase(0, 0, 0)]
        [TestCase(15, 0, 15)]
        [TestCase(16, 1, 0)]
        public void RegionCoordinates_UseFloorDivision(
            int chunk,
            int expectedRegion,
            int expectedLocal)
        {
            Assert.That(RegionCoordinates.FloorDivide(chunk), Is.EqualTo(expectedRegion));
            Assert.That(RegionCoordinates.FloorModulo(chunk), Is.EqualTo(expectedLocal));
        }

        [Test]
        public void RoundTrip_AcrossPositiveAndNegativeRegions_RetainsIdAndState()
        {
            var values = new Dictionary<int3, Block>
            {
                [new int3(-1, -1, -1)] = new Block(0x11111111, 7),
                [new int3(256, 0, 0)] = new Block(0x22222222, uint.MaxValue),
                [new int3(-273, 32, 17)] = new Block(0x33333333, 19)
            };

            using (var source = new WorldCore())
            {
                var session = new WorldStorageSession();
                foreach (var pair in values)
                    source.Enqueue(WorldCommand.SetBlock(pair.Key, pair.Value));
                source.Flush();
                session.TrackDirty(source);

                var save = session.SaveDirty(source, CreateStorage());
                Assert.That(save.DirtySnapshotsAccepted, Is.EqualTo(3));
                Assert.That(session.DirtyChunkCount, Is.Zero);
            }

            var snapshot = CreateStorage().LoadAll();
            Assert.That(snapshot.ChunkCount, Is.EqualTo(3));
            using (var loaded = WorldStorageWorldBuilder.CreateWorld(snapshot))
            {
                foreach (var pair in values)
                    Assert.That(loaded.GetBlockOrAir(pair.Key), Is.EqualTo(pair.Value));
            }
        }

        [Test]
        public void Json_IsSparseDeterministicAndUsesAuthoritativeNumericIds()
        {
            using (var world = new WorldCore())
            {
                var session = new WorldStorageSession();
                world.Enqueue(WorldCommand.SetBlock(new int3(3, 0, 0), new Block(30, 300)));
                world.Enqueue(WorldCommand.SetBlock(new int3(1, 0, 0), new Block(10, 100)));
                world.Enqueue(WorldCommand.RemoveBlock(new int3(2, 0, 0)));
                world.Flush();
                session.TrackDirty(world);
                session.SaveDirty(world, CreateStorage());
            }

            var path = Path.Combine(temporaryDirectory, "r.0.0.0.json");
            var json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("\"formatVersion\": 1"));
            Assert.That(json, Does.Contain($"\"hashScheme\": \"{StableBlockId.PersistenceScheme}\""));
            Assert.That(json, Does.Contain("\"region\": ["));
            Assert.That(json, Does.Not.Contain("\"id\": 0"));
            Assert.That(json.IndexOf("\"index\": 1", StringComparison.Ordinal),
                Is.LessThan(json.IndexOf("\"index\": 3", StringComparison.Ordinal)));

            var first = json;
            var snapshot = CreateStorage().LoadAll();
            CreateStorage().SaveRegion(int3.zero, snapshot.Chunks);
            Assert.That(File.ReadAllText(path), Is.EqualTo(first));
        }

        [Test]
        public void RemovingLastBlock_DeletesChunkAndRegionFile()
        {
            var storage = CreateStorage();
            var session = new WorldStorageSession();
            using (var world = new WorldCore())
            {
                world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(7, 9)));
                world.Flush();
                session.TrackDirty(world);
                session.SaveDirty(world, storage);
                Assert.That(File.Exists(Path.Combine(temporaryDirectory, "r.0.0.0.json")), Is.True);

                world.Enqueue(WorldCommand.RemoveBlock(int3.zero));
                world.Flush();
                session.TrackDirty(world);
                session.SaveDirty(world, storage);
            }

            Assert.That(File.Exists(Path.Combine(temporaryDirectory, "r.0.0.0.json")), Is.False);
            Assert.That(CreateStorage().LoadAll().ChunkCount, Is.Zero);
        }

        [Test]
        public void SavingOneLoadedChunk_PreservesUnloadedChunkInSameRegion()
        {
            var storage = CreateStorage();
            using (var initial = new WorldCore())
            {
                var session = new WorldStorageSession();
                initial.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 10)));
                initial.Enqueue(WorldCommand.SetBlock(new int3(16, 0, 0), new Block(2, 20)));
                initial.Flush();
                session.TrackDirty(initial);
                session.SaveDirty(initial, storage);
            }

            using (var partiallyLoaded = new WorldCore())
            {
                var session = new WorldStorageSession();
                partiallyLoaded.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(3, 30)));
                partiallyLoaded.Flush();
                session.TrackDirty(partiallyLoaded);
                session.SaveDirty(partiallyLoaded, storage);
            }

            using (var loaded = WorldStorageWorldBuilder.CreateWorld(CreateStorage().LoadAll()))
            {
                Assert.That(loaded.GetBlockOrAir(int3.zero), Is.EqualTo(new Block(3, 30)));
                Assert.That(
                    loaded.GetBlockOrAir(new int3(16, 0, 0)),
                    Is.EqualTo(new Block(2, 20)));
            }
        }

        [Test]
        public void CorruptJson_ThrowsAndDoesNotMutateLiveWorldHost()
        {
            var gameObject = new GameObject("Storage Test Host");
            try
            {
                var host = gameObject.AddComponent<WorldHost>();
                var position = new int3(4, 5, 6);
                var original = new Block(99, 123);
                host.Enqueue(WorldCommand.SetBlock(position, original));
                host.Flush();

                File.WriteAllText(
                    Path.Combine(temporaryDirectory, "r.0.0.0.json"),
                    "{ this is not valid JSON");

                Assert.Throws<WorldStorageException>(() => host.LoadWorld(temporaryDirectory));
                Assert.That(host.GetBlockOrAir(position), Is.EqualTo(original));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void WorldHost_ManualSaveAndLoad_RoundTripsValidatedReplacement()
        {
            var gameObject = new GameObject("Storage Round Trip Host");
            try
            {
                var host = gameObject.AddComponent<WorldHost>();
                var negativePosition = new int3(-1, 2, -1);
                var positivePosition = new int3(256, 3, 4);
                var negativeBlock = new Block(71, 17);
                var positiveBlock = new Block(72, 18);

                host.Enqueue(WorldCommand.SetBlock(negativePosition, negativeBlock));
                host.Enqueue(WorldCommand.SetBlock(positivePosition, positiveBlock));
                host.Flush();
                var saved = host.SaveWorld(temporaryDirectory);
                Assert.That(saved.DirtySnapshotsAccepted, Is.EqualTo(2));

                host.Enqueue(WorldCommand.RemoveBlock(negativePosition));
                host.Enqueue(WorldCommand.RemoveBlock(positivePosition));
                host.Flush();
                Assert.That(host.GetBlockOrAir(negativePosition), Is.EqualTo(Block.Air));

                var loaded = host.LoadWorld(temporaryDirectory);
                Assert.That(loaded.ChunkCount, Is.EqualTo(2));
                Assert.That(host.GetBlockOrAir(negativePosition), Is.EqualTo(negativeBlock));
                Assert.That(host.GetBlockOrAir(positivePosition), Is.EqualTo(positiveBlock));
                Assert.That(host.DirtyStorageChunkCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ManualSave_RecreatedHost_LoadReconstructsBlockAndRenderedMesh()
        {
            var previewScene = EditorSceneManager.NewPreviewScene();
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistryManifest>(
                BlockRegistryManifestBuilder.ManifestPath);
            var renderManifest = AssetDatabase.LoadAssetAtPath<BlockRenderManifest>(
                BlockRenderManifestBuilder.RenderManifestPath);
            var materials = AssetDatabase.LoadAssetAtPath<ChunkMaterialSet>(
                BlockRenderManifestBuilder.MaterialSetPath);
            var position = new int3(-1, 2, -1);
            var block = new Block(StableBlockId.Compute("minecraft:stone"), 27);

            try
            {
                var sourceObject = new GameObject("Storage Restart Source");
                SceneManager.MoveGameObjectToScene(sourceObject, previewScene);
                var source = sourceObject.AddComponent<WorldHost>();
                source.ConfigureAssets(registry, renderManifest, materials);
                source.Enqueue(WorldCommand.SetBlock(position, block));
                source.Flush();
                source.SaveWorld(temporaryDirectory);
                UnityEngine.Object.DestroyImmediate(sourceObject);

                var loadedObject = new GameObject("Storage Restart Loaded");
                SceneManager.MoveGameObjectToScene(loadedObject, previewScene);
                var loaded = loadedObject.AddComponent<WorldHost>();
                loaded.ConfigureAssets(registry, renderManifest, materials);
                var snapshot = loaded.LoadWorld(temporaryDirectory);

                Assert.That(snapshot.ChunkCount, Is.EqualTo(1));
                Assert.That(loaded.GetBlockOrAir(position), Is.EqualTo(block));
                var renderer = loadedObject.GetComponentInChildren<ChunkRenderer>(true);
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.AppliedHandle.IsValid, Is.True);
                Assert.That(renderer.Mesh.vertexCount, Is.EqualTo(24));
                UnityEngine.Object.DestroyImmediate(loadedObject);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        [Test]
        public void DuplicateBlockIndex_IsRejected()
        {
            var json = ValidRegionJson(
                "{ \"index\": 1, \"id\": 1, \"state\": 0 }," +
                "{ \"index\": 1, \"id\": 2, \"state\": 0 }");
            File.WriteAllText(Path.Combine(temporaryDirectory, "r.0.0.0.json"), json);

            var exception = Assert.Throws<WorldStorageException>(() => CreateStorage().LoadAll());
            Assert.That(exception.Message, Does.Contain("duplicate block index 1"));
        }

        [TestCase(-1, 1U)]
        [TestCase(4096, 1U)]
        [TestCase(0, 0U)]
        public void InvalidIndexOrAirEntry_IsRejected(int index, uint id)
        {
            var json = ValidRegionJson(
                $"{{ \"index\": {index}, \"id\": {id}, \"state\": 0 }}");
            File.WriteAllText(Path.Combine(temporaryDirectory, "r.0.0.0.json"), json);

            Assert.Throws<WorldStorageException>(() => CreateStorage().LoadAll());
        }

        [Test]
        public void RegistryValidator_RejectsUnknownStableId()
        {
            var manifest = ScriptableObject.CreateInstance<BlockRegistryManifest>();
            try
            {
                manifest.ReplaceEntries(new[]
                {
                    new BlockRegistryManifest.Entry("minecraft:stone", null)
                });
                var unknownId = StableBlockId.Compute("minecraft:dirt");
                File.WriteAllText(
                    Path.Combine(temporaryDirectory, "r.0.0.0.json"),
                    ValidRegionJson(
                        $"{{ \"index\": 0, \"id\": {unknownId}, \"state\": 0 }}"));

                var storage = new RegionJsonWorldStorage(
                    temporaryDirectory,
                    StoredBlockValidator.FromManifest(manifest));
                Assert.Throws<WorldStorageException>(() => storage.LoadAll());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void MismatchedRegionAndHashScheme_AreRejected()
        {
            var path = Path.Combine(temporaryDirectory, "r.0.0.0.json");
            File.WriteAllText(path, ValidRegionJson("{ \"index\": 0, \"id\": 1, \"state\": 0 }")
                .Replace("\"region\": [0, 0, 0]", "\"region\": [1, 0, 0]"));
            Assert.Throws<WorldStorageException>(() => CreateStorage().LoadAll());

            File.WriteAllText(path, ValidRegionJson("{ \"index\": 0, \"id\": 1, \"state\": 0 }")
                .Replace(StableBlockId.PersistenceScheme, "unknown-scheme"));
            Assert.Throws<WorldStorageException>(() => CreateStorage().LoadAll());
        }

        [Test]
        public void LeftoverTemporaryFile_IsReportedButValidPrimaryRemainsTruth()
        {
            using (var world = new WorldCore())
            {
                var session = new WorldStorageSession();
                world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(42, 8)));
                world.Flush();
                session.TrackDirty(world);
                session.SaveDirty(world, CreateStorage());
            }

            var primary = Path.Combine(temporaryDirectory, "r.0.0.0.json");
            File.WriteAllText(primary + RegionJsonWorldStorage.TemporarySuffix, "incomplete");
            var storage = CreateStorage();

            Assert.That(storage.RecoveryArtifacts.Count, Is.EqualTo(1));
            Assert.That(storage.RecoveryArtifacts[0].Kind, Is.EqualTo(RecoveryArtifactKind.Temporary));
            using (var loaded = WorldStorageWorldBuilder.CreateWorld(storage.LoadAll()))
                Assert.That(loaded.GetBlockOrAir(int3.zero), Is.EqualTo(new Block(42, 8)));
            Assert.Throws<WorldStorageException>(() => storage.SaveRegion(
                int3.zero,
                storage.LoadAll().Chunks));
            Assert.That(File.Exists(primary), Is.True);
        }

        [Test]
        public void OrphanedBackup_RequiresExplicitRecovery()
        {
            var backup = Path.Combine(temporaryDirectory, "r.0.0.0.json.bak");
            File.WriteAllText(backup, ValidRegionJson(
                "{ \"index\": 0, \"id\": 1, \"state\": 0 }"));

            var storage = CreateStorage();
            Assert.That(storage.RecoveryArtifacts.Single().Kind, Is.EqualTo(RecoveryArtifactKind.Backup));
            Assert.Throws<WorldStorageException>(() => storage.LoadAll());
            Assert.That(File.Exists(backup), Is.True);
        }

        [Test]
        public void FailedSave_RetainsDirtyVersion()
        {
            using (var world = new WorldCore())
            {
                var session = new WorldStorageSession();
                world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1)));
                world.Flush();
                session.TrackDirty(world);

                Assert.Throws<IOException>(() => session.SaveDirty(world, new ThrowingStorage()));
                Assert.That(session.DirtyChunkCount, Is.EqualTo(1));
                Assert.That(session.IsDirty(int3.zero), Is.True);
            }
        }

        [Test]
        public void ChunkChangedDuringSave_RetainsNewDirtyVersion()
        {
            using (var world = new WorldCore())
            {
                var session = new WorldStorageSession();
                world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1)));
                world.Flush();
                session.TrackDirty(world);

                var storage = new CallbackStorage(() =>
                {
                    world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(2)));
                    world.Flush();
                });
                var result = session.SaveDirty(world, storage);

                Assert.That(result.DirtySnapshotsAccepted, Is.Zero);
                Assert.That(result.DirtySnapshotsRetained, Is.EqualTo(1));
                Assert.That(session.IsDirty(int3.zero), Is.True);
                Assert.That(world.GetBlockOrAir(int3.zero), Is.EqualTo(new Block(2)));
                Assert.That(session.TryUnloadCleanChunk(world, int3.zero), Is.False);
                Assert.That(world.GetBlockOrAir(int3.zero), Is.EqualTo(new Block(2)));
            }
        }

        private RegionJsonWorldStorage CreateStorage()
        {
            return new RegionJsonWorldStorage(temporaryDirectory);
        }

        private static string ValidRegionJson(string blocks)
        {
            return "{\n" +
                   "  \"formatVersion\": 1,\n" +
                   $"  \"hashScheme\": \"{StableBlockId.PersistenceScheme}\",\n" +
                   "  \"region\": [0, 0, 0],\n" +
                   "  \"chunks\": [\n" +
                   "    { \"local\": [0, 0, 0], \"blocks\": [" + blocks + "] }\n" +
                   "  ]\n" +
                   "}\n";
        }

        private sealed class ThrowingStorage : IWorldStorage
        {
            public IReadOnlyList<RecoveryArtifact> RecoveryArtifacts =>
                Array.Empty<RecoveryArtifact>();

            public WorldStorageSnapshot LoadAll()
            {
                throw new NotSupportedException();
            }

            public IReadOnlyList<StoredChunk> LoadRegion(int3 regionCoordinate)
            {
                throw new NotSupportedException();
            }

            public void SaveRegion(int3 regionCoordinate, IReadOnlyList<StoredChunk> replacementChunks)
            {
                throw new IOException("Simulated disk failure.");
            }
        }

        private sealed class CallbackStorage : IWorldStorage
        {
            private readonly Action callback;

            public CallbackStorage(Action callback)
            {
                this.callback = callback;
            }

            public IReadOnlyList<RecoveryArtifact> RecoveryArtifacts =>
                Array.Empty<RecoveryArtifact>();

            public WorldStorageSnapshot LoadAll()
            {
                throw new NotSupportedException();
            }

            public IReadOnlyList<StoredChunk> LoadRegion(int3 regionCoordinate)
            {
                return Array.Empty<StoredChunk>();
            }

            public void SaveRegion(int3 regionCoordinate, IReadOnlyList<StoredChunk> replacementChunks)
            {
                callback();
            }
        }
    }
}
