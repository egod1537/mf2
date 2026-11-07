using System.Collections.Generic;
using System.Linq;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using MF2.Entities.Player;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.Game.Physics;
using UnityEngine;

namespace MF2.Game.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(GameCommandNetworkAdapter), typeof(ServerSessionCoordinator))]
    [RequireComponent(typeof(WorldReplicationAdapter))]
    public sealed class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkObject playerPrefab;

        private readonly Dictionary<int, NetworkObject> players = new();
        private readonly ConnectionSpawnGuard spawnGuard = new();
        private readonly Dictionary<int, PendingSpawn> pendingSpawns = new();
        private GameCommandNetworkAdapter commandAdapter;
        private NetworkManager networkManager;
        private ServerSessionCoordinator sessionCoordinator;
        private WorldReplicationAdapter worldReplication;
        private ServerWorldCollisionBridge serverCollision;
        private readonly SpawnLocator spawnLocator = new();

        public void Configure(NetworkObject prefab) => playerPrefab = prefab;

        private void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            commandAdapter = GetComponent<GameCommandNetworkAdapter>();
            sessionCoordinator = GetComponent<ServerSessionCoordinator>();
            worldReplication = GetComponent<WorldReplicationAdapter>();
            serverCollision = GetComponent<ServerWorldCollisionBridge>();
            if (serverCollision == null)
                serverCollision = gameObject.AddComponent<ServerWorldCollisionBridge>();
        }

        private void Start()
        {
            networkManager.SceneManager.OnClientPresenceChangeEnd += OnClientPresenceChanged;
            networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            worldReplication.ConnectionWorldReady += OnConnectionWorldReady;
            serverCollision.ReadinessChanged += OnServerCollisionReadinessChanged;
        }

        private void OnClientPresenceChanged(ClientPresenceChangeEventArgs args)
        {
            if (!networkManager.IsServerStarted || !IsPlayerScene(args.Scene.name))
                return;
            if (args.Added)
                SpawnWhenWorldReady(args.Connection, args.Scene);
            else
            {
                pendingSpawns.Remove(args.Connection.ClientId);
                worldReplication.InvalidateConnectionWorldReady(args.Connection);
                Despawn(
                    args.Connection.ClientId,
                    unregisterDomainPlayer: false,
                    expectedScene: args.Scene);
            }
        }

        private void SpawnWhenWorldReady(
            NetworkConnection connection,
            UnityEngine.SceneManagement.Scene scene)
        {
            if (!sessionCoordinator.TryGetSession(out var activeSession) ||
                !worldReplication.IsConnectionWorldReady(connection, activeSession) ||
                !serverCollision.IsReadyFor(activeSession))
            {
                pendingSpawns[connection.ClientId] = new PendingSpawn(connection, scene);
                return;
            }

            pendingSpawns.Remove(connection.ClientId);
            SpawnFor(connection, scene);
        }

        private void OnServerCollisionReadinessChanged()
        {
            foreach (var pending in pendingSpawns.Values.ToArray())
                SpawnWhenWorldReady(pending.Connection, pending.Scene);
        }

        private void OnConnectionWorldReady(NetworkConnection connection)
        {
            if (connection == null ||
                !pendingSpawns.TryGetValue(connection.ClientId, out var pending) ||
                !pending.Scene.IsValid() || !pending.Scene.isLoaded)
            {
                return;
            }

            SpawnWhenWorldReady(connection, pending.Scene);
        }

        private void SpawnFor(NetworkConnection connection, UnityEngine.SceneManagement.Scene scene)
        {
            if (playerPrefab == null || !connection.IsActive || !connection.IsAuthenticated)
                return;

            if (players.TryGetValue(connection.ClientId, out var previousPlayer) &&
                previousPlayer != null &&
                previousPlayer.gameObject.scene != scene)
            {
                Despawn(
                    connection.ClientId,
                    unregisterDomainPlayer: false,
                    expectedScene: previousPlayer.gameObject.scene);
            }
            if (!spawnGuard.TryReserve(connection.ClientId))
                return;

            var hadDomainPlayer = commandAdapter.TryGetPlayerId(
                connection.ClientId,
                out _);
            if (!commandAdapter.TryRegisterConnection(connection.ClientId, out var playerId))
            {
                spawnGuard.Release(connection.ClientId);
                Debug.LogError($"[MF2] Domain player registration failed for connection {connection.ClientId}.");
                return;
            }

            NetworkObject instance = null;
            IPhysicsBodyRegistry physicsBodies = null;
            IPlayerNetworkObjectMap networkObjects = null;
            Rigidbody registeredBody = null;
            var bodyRegistered = false;
            var objectRegistered = false;
            try
            {
                var spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Where(point => point.gameObject.scene == scene)
                    .OrderBy(point => point.name)
                    .ToArray();
                var index = spawnPoints.Length == 0 ? 0 : Mathf.Abs(connection.ClientId) % spawnPoints.Length;
                var position = spawnPoints.Length == 0 ? Vector3.up * 2f : spawnPoints[index].transform.position;
                var rotation = spawnPoints.Length == 0 ? Quaternion.identity : spawnPoints[index].transform.rotation;
                if (sessionCoordinator.TryGetSession(out var activeSession) &&
                    activeSession.Terrain != null)
                {
                    if (!spawnLocator.TryLocate(
                            activeSession.World,
                            activeSession.Terrain.Config,
                            Mathf.Max(0, connection.ClientId),
                            out var terrainPosition))
                    {
                        throw new System.InvalidOperationException(
                            "No safe spawn column was found in the authoritative terrain.");
                    }
                    position = new Vector3(terrainPosition.x, terrainPosition.y, terrainPosition.z);
                    rotation = Quaternion.identity;
                }
                instance = Instantiate(playerPrefab, position, rotation);
                var controller = instance.GetComponent<BlockPlayerController>();
                if (controller == null)
                    throw new MissingComponentException("The player prefab requires BlockPlayerController.");
                if (!sessionCoordinator.TryGetSession(out activeSession))
                    throw new System.InvalidOperationException("The server session stopped during player spawn.");
                controller.BindSpawn(new PlayerSpawnContext(
                    activeSession.Id,
                    playerId,
                    connection.ClientId,
                    position,
                    rotation));
                registeredBody = controller.GetComponent<Rigidbody>();
                if (registeredBody == null)
                    throw new MissingComponentException("The player prefab requires Rigidbody.");
                if (!sessionCoordinator.TryGetPhysicsBodyRegistry(out physicsBodies) ||
                    !physicsBodies.TryRegisterBody(playerId, registeredBody))
                {
                    throw new System.InvalidOperationException(
                        $"Physics body registration failed for player {playerId}.");
                }
                bodyRegistered = true;
                if (!sessionCoordinator.TryGetNetworkObjectMap(out networkObjects) ||
                    !networkObjects.TryRegister(playerId, instance))
                {
                    throw new System.InvalidOperationException(
                        $"NetworkObject registration failed for player {playerId}.");
                }
                objectRegistered = true;
                networkManager.ServerManager.Spawn(instance, connection, scene);
                players[connection.ClientId] = instance;
            }
            catch
            {
                if (objectRegistered)
                    networkObjects?.Unregister(playerId, instance);
                if (bodyRegistered)
                    physicsBodies?.UnregisterBody(playerId, registeredBody);
                if (!hadDomainPlayer)
                    commandAdapter.UnregisterConnection(connection.ClientId);
                spawnGuard.Release(connection.ClientId);
                if (instance != null)
                    Destroy(instance.gameObject);
                throw;
            }
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;
            pendingSpawns.Remove(connection.ClientId);
            Despawn(connection.ClientId, unregisterDomainPlayer: true);
        }

        private void Despawn(
            int connectionId,
            bool unregisterDomainPlayer,
            UnityEngine.SceneManagement.Scene expectedScene = default)
        {
            if (expectedScene.IsValid() &&
                players.TryGetValue(connectionId, out var trackedPlayer) &&
                trackedPlayer != null &&
                trackedPlayer.gameObject.scene != expectedScene)
            {
                return;
            }

            spawnGuard.Release(connectionId);
            players.Remove(connectionId, out var player);
            UnregisterRuntimeBindings(connectionId, player);
            if (unregisterDomainPlayer)
                commandAdapter.UnregisterConnection(connectionId);
            if (player == null)
                return;
            if (networkManager.IsServerStarted && player.IsSpawned)
                networkManager.ServerManager.Despawn(player);
            else
                Destroy(player.gameObject);
        }

        private static bool IsPlayerScene(string sceneName) =>
            sceneName == SessionController.TestScene || sceneName == SessionController.IngameScene;

        private void UnregisterRuntimeBindings(int connectionId, NetworkObject player)
        {
            var playerId = default(PlayerId);
            var hasPlayerId = player != null &&
                              player.TryGetComponent<BlockPlayerController>(out var controller) &&
                              controller.TryGetPlayerId(out playerId);
            if (!hasPlayerId)
                hasPlayerId = commandAdapter.TryGetPlayerId(connectionId, out playerId);
            if (!hasPlayerId)
                return;

            if (sessionCoordinator.TryGetNetworkObjectMap(out var networkObjects))
                networkObjects.Unregister(playerId, player);
            if (sessionCoordinator.TryGetPhysicsBodyRegistry(out var physicsBodies))
            {
                var body = player == null ? null : player.GetComponent<Rigidbody>();
                physicsBodies.UnregisterBody(playerId, body);
            }
        }

        private void OnDestroy()
        {
            if (networkManager == null)
                return;
            if (networkManager.SceneManager != null)
                networkManager.SceneManager.OnClientPresenceChangeEnd -= OnClientPresenceChanged;
            if (networkManager.ServerManager != null)
                networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            if (worldReplication != null)
                worldReplication.ConnectionWorldReady -= OnConnectionWorldReady;
            if (serverCollision != null)
                serverCollision.ReadinessChanged -= OnServerCollisionReadinessChanged;
            foreach (var pair in players.ToArray())
            {
                UnregisterRuntimeBindings(pair.Key, pair.Value);
                commandAdapter.UnregisterConnection(pair.Key);
            }
            spawnGuard.Clear();
            pendingSpawns.Clear();
            players.Clear();
        }

        private readonly struct PendingSpawn
        {
            public PendingSpawn(
                NetworkConnection connection,
                UnityEngine.SceneManagement.Scene scene)
            {
                Connection = connection;
                Scene = scene;
            }

            public NetworkConnection Connection { get; }
            public UnityEngine.SceneManagement.Scene Scene { get; }
        }
    }
}
