using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Managing;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.Game.Persistence;
using MF2.World.Core;
using MF2.World.Registry;
using MF2.World.Storage;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MF2.Tests
{
    public sealed class SessionPersistenceTests
    {
        [Test]
        public void SaveAndLoad_ReuseWorldStorageSessionAndBuilder()
        {
            var storage = new MemoryWorldStorage();
            var position = new int3(18, 2, -3);
            var block = new Block(21, 4);

            using (var sourceWorld = new SimulationWorld())
            using (var source = CreateSession(sourceWorld))
            {
                var player = new PlayerId(Guid.NewGuid());
                source.Start();
                source.TryRegisterPlayer(player);
                source.SubmitCommand(player, GameCommand.ApplyWorld(
                    WorldCommand.SetBlock(position, block)));
                source.Step(new SimulationStep(0f));

                var result = source.SaveWorld(storage);

                Assert.That(result.DirtySnapshotsAccepted, Is.EqualTo(1));
                Assert.That(source.CaptureSnapshot().DirtyStorageChunkCount, Is.Zero);
            }

            using (var loadedWorld = new SimulationWorld())
            using (var loaded = CreateSession(loadedWorld))
            {
                loaded.LoadWorld(storage);

                Assert.That(loaded.World.GetBlockOrAir(position), Is.EqualTo(block));
                Assert.That(loaded.CaptureSnapshot().DirtyStorageChunkCount, Is.Zero);
            }
        }

        [Test]
        public void Restore_SwapsOnlyAfterReplacementWasBuilt()
        {
            using (var simulation = new SimulationWorld())
            {
                var original = simulation.Core;
                var stored = new StoredChunk(
                    new int3(0, 0, 0),
                    new[] { new StoredBlock(0, 7, 0) });

                simulation.Restore(new WorldStorageSnapshot(new[] { stored }));

                Assert.That(simulation.Core, Is.Not.SameAs(original));
                Assert.That(simulation.Core.GetBlockOrAir(int3.zero), Is.EqualTo(new Block(7, 0)));
                Assert.Throws<ObjectDisposedException>(() => _ = original.LoadedChunkCount);
            }
        }

        [Test]
        public void FailedLoad_DoesNotReplaceLiveWorld()
        {
            using (var simulation = new SimulationWorld())
            {
                var original = simulation.Core;
                var storage = new ThrowingWorldStorage();

                Assert.Throws<WorldStorageException>(() => simulation.Load(storage));
                Assert.That(simulation.Core, Is.SameAs(original));
            }
        }

        [Test]
        public void SessionArchive_RoundTripsIdentityTickPlayersPhaseAndRegionWorld()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var storage = new SessionJsonStorage(directory);
                var player = new PlayerId(Guid.NewGuid());
                var position = new int3(-18, 4, 33);
                var block = new Block(BlockTraitsCatalog.WaterStableId, 9);
                SessionArchiveSnapshot captured;

                using (var sourceWorld = new SimulationWorld())
                {
                    var registry = new PlayerRegistry();
                    using (var source = new GameSession(
                               registry,
                               sourceWorld,
                               new CommandProcessor(),
                               new SessionClock()))
                    {
                        source.Start();
                        source.TryRegisterPlayer(player);
                        new HealthSystem(registry).ApplyDamage(player, 35);
                        source.SubmitCommand(player, GameCommand.ApplyWorld(
                            WorldCommand.SetBlock(position, block)));
                        source.Step(new SimulationStep(0.25f));
                        source.Pause();
                        captured = source.CaptureArchive();
                        storage.Save(captured);
                    }
                }

                var loaded = storage.LoadCurrent();
                Assert.That(loaded.Id, Is.EqualTo(captured.Id));
                Assert.That(loaded.Generation, Is.EqualTo(captured.Generation));
                Assert.That(loaded.Phase, Is.EqualTo(SessionPhase.Paused));
                Assert.That(loaded.TickIndex, Is.EqualTo(1));
                Assert.That(loaded.ElapsedSeconds, Is.EqualTo(0.25d).Within(0.00001d));
                Assert.That(loaded.Players.Count, Is.EqualTo(1));
                Assert.That(loaded.Players[0].Health, Is.EqualTo(65));

                using (var restoredWorld = new SimulationWorld())
                using (var restored = CreateSession(restoredWorld))
                {
                    restored.Restore(loaded);
                    Assert.That(restored.Id, Is.EqualTo(captured.Id));
                    Assert.That(restored.Generation, Is.GreaterThan(captured.Generation));
                    Assert.That(restored.TickIndex, Is.EqualTo(1));
                    Assert.That(restored.Players.TryGet(player, out var restoredPlayer), Is.True);
                    Assert.That(restoredPlayer.Health, Is.EqualTo(65));
                    Assert.That(restored.World.GetBlockOrAir(position), Is.EqualTo(block));
                }
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [Test]
        public void UnsupportedPointerVersion_IsRejectedWithoutChangingLiveSession()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var storage = new SessionJsonStorage(directory);
                SessionId liveId;
                using (var world = new SimulationWorld())
                using (var live = CreateSession(world))
                {
                    live.Start();
                    liveId = live.Id;
                    storage.Save(live.CaptureArchive());
                    var pointer = File.ReadAllText(storage.CurrentPointerPath);
                    File.WriteAllText(
                        storage.CurrentPointerPath,
                        pointer.Replace(
                            $"\"FormatVersion\": {SessionJsonStorage.FormatVersion}",
                            "\"FormatVersion\": 999"));

                    Assert.Throws<SessionStorageException>(() => storage.LoadCurrent());
                    Assert.That(live.Id, Is.EqualTo(liveId));
                    Assert.That(live.IsRunning, Is.True);
                }
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [Test]
        public void SecondCommit_PreservesExplicitlyLoadablePreviousPointerBackup()
        {
            var directory = CreateTemporaryDirectory();
            try
            {
                var storage = new SessionJsonStorage(directory);
                SessionArchiveSnapshot first;
                SessionArchiveSnapshot second;
                using (var firstWorld = new SimulationWorld())
                using (var firstSession = CreateSession(firstWorld))
                using (var secondWorld = new SimulationWorld())
                using (var secondSession = CreateSession(secondWorld))
                {
                    firstSession.Start();
                    secondSession.Start();
                    first = firstSession.CaptureArchive();
                    second = secondSession.CaptureArchive();
                }

                storage.Save(first);
                storage.Save(second);

                Assert.That(storage.LoadCurrent().Id, Is.EqualTo(second.Id));
                Assert.That(storage.LoadBackup().Id, Is.EqualTo(first.Id));
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [TestCase(CommittedArchiveDamage.MissingPointer)]
        [TestCase(CommittedArchiveDamage.MissingManifest)]
        [TestCase(CommittedArchiveDamage.CorruptManifest)]
        [TestCase(CommittedArchiveDamage.CorruptRegion)]
        [TestCase(CommittedArchiveDamage.UnsupportedSchema)]
        public void InvalidArchiveRestore_LeavesLiveCoordinatorSessionUnchanged(
            CommittedArchiveDamage damage)
        {
            var directory = CreateTemporaryDirectory();
            var networkObject = new GameObject("Persistence NetworkManager Test");
            var coordinatorObject = new GameObject("Persistence Coordinator Test");
            var networkManager = networkObject.AddComponent<NetworkManager>();
            var coordinator = coordinatorObject.AddComponent<ServerSessionCoordinator>();
            LifetimeScope application = null;

            try
            {
                application = LifetimeScope.Create(
                    builder =>
                    {
                        builder.RegisterInstance(networkManager);
                        builder.RegisterInstance<IBlockIdCatalog>(new PermissiveBlockCatalog());
                    },
                    "Persistence Application Scope Test");
                if (application.Container == null)
                    application.Build();
                application.Container.Inject(coordinator);
                var live = coordinator.BeginServerSession();

                SessionArchiveSnapshot archive;
                using (var sourceWorld = new SimulationWorld())
                using (var source = CreateSession(sourceWorld))
                {
                    var player = new PlayerId(Guid.NewGuid());
                    source.Start();
                    source.TryRegisterPlayer(player);
                    source.SubmitCommand(player, GameCommand.ApplyWorld(
                        WorldCommand.SetBlock(int3.zero, new Block(19))));
                    source.Step(new SimulationStep(0f));
                    archive = source.CaptureArchive();
                }

                var storage = new SessionJsonStorage(directory);
                if (damage != CommittedArchiveDamage.MissingPointer)
                    storage.Save(archive);
                DamageCommittedArchive(storage, damage);

                var persistence = new SessionPersistenceCoordinator(coordinator);
                Assert.Throws<SessionStorageException>(() => persistence.RestoreCurrent(storage));
                Assert.That(coordinator.Session, Is.SameAs(live));
                Assert.That(live.IsRunning, Is.True);
                Assert.That(live.Phase, Is.EqualTo(SessionPhase.Running));
            }
            finally
            {
                coordinator.EndServerSession();
                application?.DisposeCore();
                UnityEngine.Object.DestroyImmediate(coordinatorObject);
                UnityEngine.Object.DestroyImmediate(networkObject);
                if (application != null)
                    UnityEngine.Object.DestroyImmediate(application.gameObject);
                DeleteTemporaryDirectory(directory);
            }
        }

        private static void DamageCommittedArchive(
            SessionJsonStorage storage,
            CommittedArchiveDamage damage)
        {
            switch (damage)
            {
                case CommittedArchiveDamage.MissingPointer:
                    return;
                case CommittedArchiveDamage.MissingManifest:
                    File.Delete(Directory.GetFiles(
                        storage.SnapshotsDirectory,
                        "session.json",
                        SearchOption.AllDirectories).Single());
                    return;
                case CommittedArchiveDamage.CorruptManifest:
                    File.AppendAllText(
                        Directory.GetFiles(
                            storage.SnapshotsDirectory,
                            "session.json",
                            SearchOption.AllDirectories).Single(),
                        " ");
                    return;
                case CommittedArchiveDamage.CorruptRegion:
                    File.WriteAllText(
                        Directory.GetFiles(
                            storage.SnapshotsDirectory,
                            "r.*.json",
                            SearchOption.AllDirectories).Single(),
                        "{ corrupt region }");
                    return;
                case CommittedArchiveDamage.UnsupportedSchema:
                    var manifestPath = Directory.GetFiles(
                        storage.SnapshotsDirectory,
                        "session.json",
                        SearchOption.AllDirectories).Single();
                    var manifest = File.ReadAllText(manifestPath);
                    File.WriteAllText(
                        manifestPath,
                        manifest.Replace(
                            $"\"SchemaVersion\": {SessionJsonStorage.FormatVersion}",
                            "\"SchemaVersion\": 999"));
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(damage), damage, null);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "MF2-SessionPersistenceTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteTemporaryDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }

        private static GameSession CreateSession(SimulationWorld world) =>
            new GameSession(
                new PlayerRegistry(),
                world,
                new CommandProcessor(),
                new SessionClock());

        private sealed class MemoryWorldStorage : IWorldStorage
        {
            private readonly Dictionary<int3, StoredChunk> chunks =
                new Dictionary<int3, StoredChunk>();

            public IReadOnlyList<RecoveryArtifact> RecoveryArtifacts =>
                Array.Empty<RecoveryArtifact>();

            public WorldStorageSnapshot LoadAll() =>
                new WorldStorageSnapshot(chunks.Values.ToArray());

            public IReadOnlyList<StoredChunk> LoadRegion(int3 regionCoordinate) =>
                chunks.Values
                    .Where(chunk => RegionCoordinates.ToRegion(chunk.ChunkCoordinate).Equals(regionCoordinate))
                    .ToArray();

            public void SaveRegion(int3 regionCoordinate, IReadOnlyList<StoredChunk> replacementChunks)
            {
                for (var i = 0; i < replacementChunks.Count; i++)
                {
                    var chunk = replacementChunks[i];
                    if (chunk.IsEmpty)
                        chunks.Remove(chunk.ChunkCoordinate);
                    else
                        chunks[chunk.ChunkCoordinate] = chunk;
                }
            }
        }

        private sealed class ThrowingWorldStorage : IWorldStorage
        {
            public IReadOnlyList<RecoveryArtifact> RecoveryArtifacts =>
                Array.Empty<RecoveryArtifact>();

            public WorldStorageSnapshot LoadAll() =>
                throw new WorldStorageException("Corrupt snapshot.");

            public IReadOnlyList<StoredChunk> LoadRegion(int3 regionCoordinate) =>
                throw new NotSupportedException();

            public void SaveRegion(int3 regionCoordinate, IReadOnlyList<StoredChunk> replacementChunks) =>
                throw new NotSupportedException();
        }

        private sealed class PermissiveBlockCatalog : IBlockIdCatalog
        {
            public bool IsKnown(uint stableBlockId) => stableBlockId != 0;
        }

        public enum CommittedArchiveDamage : byte
        {
            MissingPointer,
            MissingManifest,
            CorruptManifest,
            CorruptRegion,
            UnsupportedSchema
        }
    }
}
