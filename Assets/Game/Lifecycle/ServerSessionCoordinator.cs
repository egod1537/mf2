using System;
using MF2.Game.Domain;
using MF2.Game.Physics;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MF2.Game.Lifecycle
{
    public enum ServerSessionCoordinatorState : byte
    {
        NoSession,
        Creating,
        Restoring,
        Running,
        Stopping
    }

    [DisallowMultipleComponent]
    public sealed class ServerSessionCoordinator : MonoBehaviour
    {
        private LifetimeScope applicationScope;
        private ServerSessionLifetimeScope sessionScope;
        private GameSession session;
        private IPhysicsBodyRegistry physicsBodies;
        private IPlayerNetworkObjectMap networkObjects;
        private SimulationWorld authoritativeWorld;
        private long generationWatermark;

        public event Action<GameSession> SessionStarted;
        public event Action<GameSession> SessionStopping;

        public ServerSessionCoordinatorState State { get; private set; } =
            ServerSessionCoordinatorState.NoSession;
        public bool HasActiveSession => session != null && session.IsRunning;
        public GameSession Session => session;

        [Inject]
        private void Construct(LifetimeScope scope)
        {
            applicationScope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        public GameSession BeginServerSession()
        {
            if (session != null)
                return session;
            if (State != ServerSessionCoordinatorState.NoSession)
                throw new InvalidOperationException($"Cannot create a server session while {State}.");
            if (applicationScope == null)
                throw new InvalidOperationException("Application lifetime scope has not been injected.");

            State = ServerSessionCoordinatorState.Creating;
            ServerSessionLifetimeScope createdScope = null;
            try
            {
                createdScope = applicationScope.CreateChild<ServerSessionLifetimeScope>(
                    childScopeName: "Server Session Lifetime Scope");
                if (createdScope.Container == null)
                    createdScope.Build();
                var createdSession = createdScope.Container.Resolve<GameSession>();
                var createdWorld = createdScope.Container.Resolve<SimulationWorld>();
                var worldInitializer = createdScope.Container.Resolve<IWorldInitializer>();
                var createdPhysicsBodies = createdScope.Container.Resolve<IPhysicsBodyRegistry>();
                var createdNetworkObjects = createdScope.Container.Resolve<IPlayerNetworkObjectMap>();
                createdSession.EnsureGenerationAfter(generationWatermark);
                var terrain = createdSession.InitializeWorld(worldInitializer);
                createdSession.Start();

                Debug.Log(
                    $"[MF2] Authoritative terrain ready: seed={terrain.Config.Seed}, " +
                    $"blocks={terrain.GeneratedBlockCount}, signature={terrain.Signature:X16}.");

                sessionScope = createdScope;
                session = createdSession;
                physicsBodies = createdPhysicsBodies;
                networkObjects = createdNetworkObjects;
                authoritativeWorld = createdWorld;
                generationWatermark = createdSession.Generation;
                State = ServerSessionCoordinatorState.Running;
                PublishSessionEvent(SessionStarted, createdSession);
                return createdSession;
            }
            catch
            {
                DisposeScope(createdScope);
                sessionScope = null;
                session = null;
                physicsBodies = null;
                networkObjects = null;
                authoritativeWorld = null;
                State = ServerSessionCoordinatorState.NoSession;
                throw;
            }
        }

        public void EndServerSession()
        {
            if (session == null)
                return;

            State = ServerSessionCoordinatorState.Stopping;
            var endingSession = session;
            var endingScope = sessionScope;
            session = null;
            sessionScope = null;
            physicsBodies = null;
            networkObjects = null;
            authoritativeWorld = null;
            try
            {
                PublishSessionEvent(SessionStopping, endingSession);
                endingSession.Stop();
            }
            finally
            {
                DisposeScope(endingScope);
                State = ServerSessionCoordinatorState.NoSession;
            }
        }

        /// <summary>
        /// Builds and restores a complete candidate scope before replacing the live session.
        /// A validation or I/O failure therefore leaves the current session untouched.
        /// </summary>
        public GameSession RestoreServerSession(SessionArchiveSnapshot archive)
        {
            if (archive == null)
                throw new ArgumentNullException(nameof(archive));
            if (State == ServerSessionCoordinatorState.Creating ||
                State == ServerSessionCoordinatorState.Restoring ||
                State == ServerSessionCoordinatorState.Stopping)
            {
                throw new InvalidOperationException($"Cannot restore a server session while {State}.");
            }
            if (applicationScope == null)
                throw new InvalidOperationException("Application lifetime scope has not been injected.");

            var previousState = session == null
                ? ServerSessionCoordinatorState.NoSession
                : ServerSessionCoordinatorState.Running;
            State = ServerSessionCoordinatorState.Restoring;
            ServerSessionLifetimeScope candidateScope = null;
            try
            {
                candidateScope = applicationScope.CreateChild<ServerSessionLifetimeScope>(
                    childScopeName: "Restored Server Session Lifetime Scope");
                if (candidateScope.Container == null)
                    candidateScope.Build();

                var candidate = candidateScope.Container.Resolve<GameSession>();
                var candidateWorld = candidateScope.Container.Resolve<SimulationWorld>();
                var candidatePhysics = candidateScope.Container.Resolve<IPhysicsBodyRegistry>();
                var candidateNetworkObjects = candidateScope.Container.Resolve<IPlayerNetworkObjectMap>();
                candidate.Restore(archive, generationWatermark);
                candidate.Start();
                if (archive.Phase == SessionPhase.Paused)
                    candidate.Pause();

                var previousSession = session;
                var previousScope = sessionScope;
                sessionScope = candidateScope;
                session = candidate;
                physicsBodies = candidatePhysics;
                networkObjects = candidateNetworkObjects;
                authoritativeWorld = candidateWorld;
                generationWatermark = candidate.Generation;
                candidateScope = null;
                State = ServerSessionCoordinatorState.Running;

                if (previousSession != null)
                {
                    PublishSessionEvent(SessionStopping, previousSession);
                    try
                    {
                        previousSession.Stop();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                    try
                    {
                        DisposeScope(previousScope);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }

                PublishSessionEvent(SessionStarted, candidate);
                return candidate;
            }
            catch
            {
                DisposeScope(candidateScope);
                State = previousState;
                throw;
            }
        }

        public bool TryGetSession(out GameSession activeSession)
        {
            activeSession = session;
            return activeSession != null && activeSession.IsRunning;
        }

        public bool TryGetPhysicsBodyRegistry(out IPhysicsBodyRegistry registry)
        {
            registry = physicsBodies;
            return registry != null && State == ServerSessionCoordinatorState.Running;
        }

        public bool TryGetNetworkObjectMap(out IPlayerNetworkObjectMap map)
        {
            map = networkObjects;
            return map != null && State == ServerSessionCoordinatorState.Running;
        }

        internal bool TryGetAuthoritativeWorldCore(out MF2.World.Core.WorldCore world)
        {
            world = authoritativeWorld?.Core;
            return world != null && State == ServerSessionCoordinatorState.Running;
        }

        private void OnDisable()
        {
            EndServerSession();
        }

        private static void DisposeScope(LifetimeScope scope)
        {
            if (scope == null)
                return;
            try
            {
                scope.DisposeCore();
            }
            finally
            {
                if (Application.isPlaying)
                    Destroy(scope.gameObject);
                else
                    DestroyImmediate(scope.gameObject);
            }
        }

        private static void PublishSessionEvent(Action<GameSession> handlers, GameSession value)
        {
            if (handlers == null)
                return;
            foreach (Action<GameSession> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(value);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
