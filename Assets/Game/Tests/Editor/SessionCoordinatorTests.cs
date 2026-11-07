using FishNet.Managing;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.Game.Networking;
using MF2.World.Core;
using Unity.Mathematics;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MF2.Tests
{
    public sealed class SessionCoordinatorTests
    {
        [Test]
        public void ServerSession_IsCreatedOnceStoppedOnceAndReplacedWithNewIdentity()
        {
            var networkObject = new GameObject("Coordinator NetworkManager Test");
            var coordinatorObject = new GameObject("Coordinator Test");
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
                    "Coordinator Application Scope Test");
                if (application.Container == null)
                    application.Build();
                application.Container.Inject(coordinator);

                Assert.That(coordinator.HasActiveSession, Is.False,
                    "A remote-client application does not create a server domain session.");

                var first = coordinator.BeginServerSession();
                var duplicate = coordinator.BeginServerSession();

                Assert.That(duplicate, Is.SameAs(first));
                Assert.That(coordinator.State, Is.EqualTo(ServerSessionCoordinatorState.Running));
                Assert.That(coordinator.HasActiveSession, Is.True);
                Assert.That(first.Generation, Is.EqualTo(1));

                coordinator.EndServerSession();
                Assert.DoesNotThrow(coordinator.EndServerSession);
                Assert.That(coordinator.State, Is.EqualTo(ServerSessionCoordinatorState.NoSession));
                Assert.That(first.Phase, Is.EqualTo(SessionPhase.Disposed));

                var second = coordinator.BeginServerSession();
                Assert.That(second.Id, Is.Not.EqualTo(first.Id));
                Assert.That(second.Generation, Is.GreaterThan(first.Generation));
            }
            finally
            {
                coordinator.EndServerSession();
                application?.DisposeCore();
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(networkObject);
                if (application != null)
                    Object.DestroyImmediate(application.gameObject);
            }
        }

        [Test]
        public void DestroyingAnUnrelatedSceneObject_DoesNotStopServerSession()
        {
            var networkObject = new GameObject("Scene Independent NetworkManager Test");
            var coordinatorObject = new GameObject("Scene Independent Coordinator Test");
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
                    "Scene Independent Application Scope Test");
                if (application.Container == null)
                    application.Build();
                application.Container.Inject(coordinator);
                var session = coordinator.BeginServerSession();

                var scenePresentation = new GameObject("Disposable Scene Presentation");
                Object.DestroyImmediate(scenePresentation);

                Assert.That(coordinator.TryGetSession(out var stillRunning), Is.True);
                Assert.That(stillRunning, Is.SameAs(session));
                Assert.That(stillRunning.Id, Is.EqualTo(session.Id));
            }
            finally
            {
                coordinator.EndServerSession();
                application?.DisposeCore();
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(networkObject);
                if (application != null)
                    Object.DestroyImmediate(application.gameObject);
            }
        }

        [Test]
        public void RestoreServerSession_SwapsOnlyAfterCandidateIsReady()
        {
            var networkObject = new GameObject("Restore Coordinator NetworkManager Test");
            var coordinatorObject = new GameObject("Restore Coordinator Test");
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
                    "Restore Application Scope Test");
                if (application.Container == null)
                    application.Build();
                application.Container.Inject(coordinator);

                var previous = coordinator.BeginServerSession();
                var archivePlayer = new PlayerId(System.Guid.NewGuid());
                SessionArchiveSnapshot archive;
                using (var sourceWorld = new SimulationWorld())
                using (var source = new GameSession(
                           new PlayerRegistry(),
                           sourceWorld,
                           new CommandProcessor(),
                           new SessionClock()))
                {
                    source.Start();
                    source.TryRegisterPlayer(archivePlayer);
                    source.SubmitCommand(archivePlayer, GameCommand.ApplyWorld(
                        WorldCommand.SetBlock(new int3(3, 2, 1), new Block(19))));
                    source.Step(new SimulationStep(0f));
                    archive = source.CaptureArchive();
                }

                var restored = coordinator.RestoreServerSession(archive);

                Assert.That(previous.Phase, Is.EqualTo(SessionPhase.Disposed));
                Assert.That(restored.Id, Is.EqualTo(archive.Id));
                Assert.That(restored.Generation, Is.GreaterThan(archive.Generation));
                Assert.That(restored.Generation, Is.GreaterThan(previous.Generation));
                Assert.That(restored.TickIndex, Is.EqualTo(archive.TickIndex));
                Assert.That(restored.World.GetBlockOrAir(new int3(3, 2, 1)),
                    Is.EqualTo(new Block(19)));
                Assert.That(coordinator.Session, Is.SameAs(restored));
            }
            finally
            {
                coordinator.EndServerSession();
                application?.DisposeCore();
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(networkObject);
                if (application != null)
                    Object.DestroyImmediate(application.gameObject);
            }
        }

        [Test]
        public void RestoredPlayerConnectionMap_RebindsWithoutDuplicatingOrRemovingDomainPlayer()
        {
            var root = new GameObject("Restored Connection Map Test");
            var networkManager = root.AddComponent<NetworkManager>();
            var coordinator = root.AddComponent<ServerSessionCoordinator>();
            var adapter = root.AddComponent<GameCommandNetworkAdapter>();
            LifetimeScope application = null;

            try
            {
                application = LifetimeScope.Create(
                    builder =>
                    {
                        builder.RegisterInstance(networkManager);
                        builder.RegisterInstance<IBlockIdCatalog>(new PermissiveBlockCatalog());
                    },
                    "Restored Connection Map Application Scope Test");
                if (application.Container == null)
                    application.Build();
                application.Container.Inject(coordinator);

                var session = coordinator.BeginServerSession();
                var restoredPlayer = new PlayerId(System.Guid.NewGuid());
                Assert.That(session.TryRegisterPlayer(restoredPlayer), Is.True);
                Assert.That(
                    adapter.TryRestoreConnection(
                        16,
                        new PlayerId(System.Guid.NewGuid()),
                        out _),
                    Is.False,
                    "A connection cannot claim an identity absent from the restored registry.");

                Assert.That(
                    adapter.TryRestoreConnection(17, restoredPlayer, out var mapped),
                    Is.True);
                Assert.That(mapped, Is.EqualTo(restoredPlayer));
                Assert.That(adapter.MappedConnectionCount, Is.EqualTo(1));
                Assert.That(
                    adapter.TryRestoreConnection(18, restoredPlayer, out _),
                    Is.False,
                    "A restored identity must not be mapped to two live connections.");

                Assert.That(adapter.ReleaseConnectionMapping(17), Is.True);
                Assert.That(adapter.MappedConnectionCount, Is.Zero);
                Assert.That(session.Players.Contains(restoredPlayer), Is.True,
                    "Releasing a transient connection map must preserve restored model state.");
            }
            finally
            {
                coordinator.EndServerSession();
                application?.DisposeCore();
                Object.DestroyImmediate(root);
                if (application != null)
                    Object.DestroyImmediate(application.gameObject);
            }
        }

        [Test]
        public void FailedSessionCreation_CleansPartialScopeAndReturnsToNoSession()
        {
            var networkObject = new GameObject("Failed Coordinator NetworkManager Test");
            var coordinatorObject = new GameObject("Failed Coordinator Test");
            var networkManager = networkObject.AddComponent<NetworkManager>();
            var coordinator = coordinatorObject.AddComponent<ServerSessionCoordinator>();
            LifetimeScope application = null;
            var scopesBefore = Object.FindObjectsByType<ServerSessionLifetimeScope>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length;

            try
            {
                application = LifetimeScope.Create(
                    builder => builder.RegisterInstance(networkManager),
                    "Failed Coordinator Application Scope Test");
                if (application.Container == null)
                    application.Build();
                application.Container.Inject(coordinator);

                Assert.That(
                    () => coordinator.BeginServerSession(),
                    Throws.Exception);
                Assert.That(coordinator.State, Is.EqualTo(ServerSessionCoordinatorState.NoSession));
                Assert.That(coordinator.Session, Is.Null);
                Assert.That(coordinator.HasActiveSession, Is.False);
                Assert.That(
                    Object.FindObjectsByType<ServerSessionLifetimeScope>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None).Length,
                    Is.EqualTo(scopesBefore));
                Assert.DoesNotThrow(coordinator.EndServerSession);
            }
            finally
            {
                coordinator.EndServerSession();
                application?.DisposeCore();
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(networkObject);
                if (application != null)
                    Object.DestroyImmediate(application.gameObject);
            }
        }

        [Test]
        public void ServerSession_ThreeStartStopCyclesKeepOneScopeAndMonotonicGeneration()
        {
            var networkObject = new GameObject("Repeated Coordinator NetworkManager Test");
            var coordinatorObject = new GameObject("Repeated Coordinator Test");
            var networkManager = networkObject.AddComponent<NetworkManager>();
            var coordinator = coordinatorObject.AddComponent<ServerSessionCoordinator>();
            LifetimeScope application = null;
            var scopesBefore = Object.FindObjectsByType<ServerSessionLifetimeScope>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length;
            var started = 0;
            var stopping = 0;
            long previousGeneration = 0;
            var identities = new System.Collections.Generic.HashSet<System.Guid>();

            try
            {
                application = LifetimeScope.Create(
                    builder =>
                    {
                        builder.RegisterInstance(networkManager);
                        builder.RegisterInstance<IBlockIdCatalog>(new PermissiveBlockCatalog());
                    },
                    "Repeated Coordinator Application Scope Test");
                if (application.Container == null)
                    application.Build();
                application.Container.Inject(coordinator);
                coordinator.SessionStarted += _ => started++;
                coordinator.SessionStopping += _ => stopping++;

                for (var cycle = 0; cycle < 3; cycle++)
                {
                    var current = coordinator.BeginServerSession();
                    Assert.That(coordinator.BeginServerSession(), Is.SameAs(current));
                    Assert.That(current.Generation, Is.GreaterThan(previousGeneration));
                    Assert.That(identities.Add(current.Id.Value), Is.True);
                    Assert.That(
                        Object.FindObjectsByType<ServerSessionLifetimeScope>(
                            FindObjectsInactive.Include,
                            FindObjectsSortMode.None).Length,
                        Is.EqualTo(scopesBefore + 1));

                    previousGeneration = current.Generation;
                    coordinator.EndServerSession();
                    Assert.That(current.Phase, Is.EqualTo(SessionPhase.Disposed));
                    Assert.That(coordinator.State, Is.EqualTo(ServerSessionCoordinatorState.NoSession));
                    Assert.That(
                        Object.FindObjectsByType<ServerSessionLifetimeScope>(
                            FindObjectsInactive.Include,
                            FindObjectsSortMode.None).Length,
                        Is.EqualTo(scopesBefore));
                }

                Assert.That(started, Is.EqualTo(3));
                Assert.That(stopping, Is.EqualTo(3));
            }
            finally
            {
                coordinator.EndServerSession();
                application?.DisposeCore();
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(networkObject);
                if (application != null)
                    Object.DestroyImmediate(application.gameObject);
            }
        }

        [Test]
        public void RestoreServerSession_ThreeTimesPublishesStopBeforeStartAndDisposesPrevious()
        {
            var networkObject = new GameObject("Repeated Restore NetworkManager Test");
            var coordinatorObject = new GameObject("Repeated Restore Coordinator Test");
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
                    "Repeated Restore Application Scope Test");
                if (application.Container == null)
                    application.Build();
                application.Container.Inject(coordinator);

                var current = coordinator.BeginServerSession();
                SessionArchiveSnapshot archive;
                using (var sourceWorld = new SimulationWorld())
                using (var source = new GameSession(
                           new PlayerRegistry(),
                           sourceWorld,
                           new CommandProcessor(),
                           new SessionClock()))
                {
                    source.Start();
                    source.Step(new SimulationStep(0f));
                    archive = source.CaptureArchive();
                }

                var events = new System.Collections.Generic.List<(string Kind, GameSession Session)>();
                coordinator.SessionStopping += value => events.Add(("stop", value));
                coordinator.SessionStarted += value => events.Add(("start", value));

                for (var cycle = 0; cycle < 3; cycle++)
                {
                    var previous = current;
                    var previousGeneration = previous.Generation;
                    current = coordinator.RestoreServerSession(archive);

                    Assert.That(previous.Phase, Is.EqualTo(SessionPhase.Disposed));
                    Assert.That(current.Generation, Is.GreaterThan(previousGeneration));
                    Assert.That(events[cycle * 2].Kind, Is.EqualTo("stop"));
                    Assert.That(events[cycle * 2].Session, Is.SameAs(previous));
                    Assert.That(events[cycle * 2 + 1].Kind, Is.EqualTo("start"));
                    Assert.That(events[cycle * 2 + 1].Session, Is.SameAs(current));
                }

                Assert.That(events.Count, Is.EqualTo(6));
                Assert.That(coordinator.Session, Is.SameAs(current));
                Assert.That(coordinator.State, Is.EqualTo(ServerSessionCoordinatorState.Running));
            }
            finally
            {
                coordinator.EndServerSession();
                application?.DisposeCore();
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(networkObject);
                if (application != null)
                    Object.DestroyImmediate(application.gameObject);
            }
        }

        private sealed class PermissiveBlockCatalog : IBlockIdCatalog
        {
            public bool IsKnown(uint stableBlockId) => stableBlockId != 0;
        }
    }
}
