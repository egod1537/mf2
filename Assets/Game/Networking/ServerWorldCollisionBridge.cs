using System;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.World.Core;
using MF2.World.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MF2.Game.Networking
{
    /// <summary>
    /// Dedicated-server projection of authoritative chunks into one MeshCollider per loaded
    /// chunk. Host physics uses the local client projection after its readiness ACK instead.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ServerSessionCoordinator))]
    public sealed class ServerWorldCollisionBridge : MonoBehaviour
    {
        private ServerSessionCoordinator coordinator;
        private GameSession session;
        private WorldCore world;
        private BlockRenderTable renderTable;
        private ChunkRendererPool rendererPool;
        private ChunkRenderPipeline renderPipeline;
        private GameObject projectionRoot;
        private bool dedicated;

        public event Action ReadinessChanged;

        public bool IsReadyFor(GameSession candidate) =>
            !dedicated ||
            candidate != null && ReferenceEquals(candidate, session) && world != null &&
            rendererPool != null && renderPipeline != null &&
            renderPipeline.AreAllLoadedChunkCollidersReady(world, rendererPool);

        private void Awake()
        {
            coordinator = GetComponent<ServerSessionCoordinator>();
            dedicated = NetworkBootstrap.IsDedicatedProcess();
        }

        private void OnEnable()
        {
            coordinator ??= GetComponent<ServerSessionCoordinator>();
            coordinator.SessionStarted += OnSessionStarted;
            coordinator.SessionStopping += OnSessionStopping;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void Start()
        {
            if (dedicated && coordinator.TryGetSession(out var activeSession))
                OnSessionStarted(activeSession);
        }

        private void OnSessionStarted(GameSession startedSession)
        {
            if (!dedicated || ReferenceEquals(session, startedSession))
                return;
            ReleaseProjection();
            session = startedSession;
            session.TickCompleted += OnSessionTickCompleted;
            TryCreateProjection();
        }

        private void OnSessionStopping(GameSession endingSession)
        {
            if (!ReferenceEquals(session, endingSession))
                return;
            session.TickCompleted -= OnSessionTickCompleted;
            session = null;
            ReleaseProjection();
            ReadinessChanged?.Invoke();
        }

        private void OnSessionTickCompleted(SessionTickResult result)
        {
            if (result.Delta.DirtyChunks.Count == 0 || world == null || renderTable == null ||
                rendererPool == null)
            {
                return;
            }
            renderPipeline.RebuildDirtyNow(world, renderTable, rendererPool);
            UnityEngine.Physics.SyncTransforms();
            ReadinessChanged?.Invoke();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (dedicated && IsPlayerScene(scene.name))
                TryCreateProjection();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (projectionRoot != null && projectionRoot.scene == scene)
            {
                ReleaseProjection();
                ReadinessChanged?.Invoke();
            }
        }

        private void TryCreateProjection()
        {
            if (!dedicated || session == null || rendererPool != null ||
                !coordinator.TryGetAuthoritativeWorldCore(out var authoritative))
            {
                return;
            }

            var hosts = FindObjectsByType<WorldHost>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            WorldHost host = null;
            for (var i = 0; i < hosts.Length; i++)
            {
                var candidate = hosts[i];
                if (candidate != null && IsPlayerScene(candidate.gameObject.scene.name) &&
                    candidate.BlockRenderManifest != null && candidate.ChunkMaterialSet != null)
                {
                    host = candidate;
                    break;
                }
            }
            if (host == null)
                return;

            world = authoritative;
            renderTable = host.BlockRenderManifest.CreateRuntimeTable();
            projectionRoot = new GameObject("Server Chunk Colliders")
            {
                hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor |
                            HideFlags.DontSaveInBuild
            };
            SceneManager.MoveGameObjectToScene(projectionRoot, host.gameObject.scene);
            projectionRoot.transform.SetParent(host.transform, false);
            rendererPool = new ChunkRendererPool(
                projectionRoot.transform,
                host.ChunkMaterialSet,
                HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild,
                renderingEnabled: false);
            renderPipeline = new ChunkRenderPipeline();
            renderPipeline.RebuildAllNow(world, renderTable, rendererPool);
            UnityEngine.Physics.SyncTransforms();
            Debug.Log(
                $"[MF2] Dedicated collision projection built: {rendererPool.ActiveCount} chunks, " +
                $"ready={renderPipeline.AreAllLoadedChunkCollidersReady(world, rendererPool)}.");
            ReadinessChanged?.Invoke();
        }

        private void ReleaseProjection()
        {
            rendererPool?.Dispose();
            rendererPool = null;
            renderPipeline = null;
            renderTable?.Dispose();
            renderTable = null;
            world = null;
            if (projectionRoot != null)
            {
                if (Application.isPlaying)
                    Destroy(projectionRoot);
                else
                    DestroyImmediate(projectionRoot);
            }
            projectionRoot = null;
        }

        private static bool IsPlayerScene(string sceneName) =>
            sceneName == SessionController.TestScene || sceneName == SessionController.IngameScene;

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (coordinator != null)
            {
                coordinator.SessionStarted -= OnSessionStarted;
                coordinator.SessionStopping -= OnSessionStopping;
            }
            if (session != null)
                session.TickCompleted -= OnSessionTickCompleted;
            session = null;
            ReleaseProjection();
        }

        private void OnDestroy()
        {
            ReadinessChanged = null;
            ReleaseProjection();
        }
    }
}
