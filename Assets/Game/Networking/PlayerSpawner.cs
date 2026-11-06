using System.Collections.Generic;
using System.Linq;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace MF2.Game.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    public sealed class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkObject playerPrefab;

        private readonly Dictionary<int, NetworkObject> players = new();
        private readonly ConnectionSpawnGuard spawnGuard = new();
        private NetworkManager networkManager;

        public void Configure(NetworkObject prefab) => playerPrefab = prefab;

        private void Awake() => networkManager = GetComponent<NetworkManager>();

        private void Start()
        {
            networkManager.SceneManager.OnClientPresenceChangeEnd += OnClientPresenceChanged;
            networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        private void OnClientPresenceChanged(ClientPresenceChangeEventArgs args)
        {
            if (!networkManager.IsServerStarted || !IsPlayerScene(args.Scene.name))
                return;
            if (args.Added)
                SpawnFor(args.Connection, args.Scene);
            else
                Despawn(args.Connection.ClientId);
        }

        private void SpawnFor(NetworkConnection connection, UnityEngine.SceneManagement.Scene scene)
        {
            if (playerPrefab == null || !connection.IsActive || !connection.IsAuthenticated)
                return;
            if (!spawnGuard.TryReserve(connection.ClientId))
                return;

            var spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(point => point.gameObject.scene == scene)
                .OrderBy(point => point.name)
                .ToArray();
            var index = spawnPoints.Length == 0 ? 0 : Mathf.Abs(connection.ClientId) % spawnPoints.Length;
            var position = spawnPoints.Length == 0 ? Vector3.up * 2f : spawnPoints[index].transform.position;
            var rotation = spawnPoints.Length == 0 ? Quaternion.identity : spawnPoints[index].transform.rotation;
            var instance = Instantiate(playerPrefab, position, rotation);
            networkManager.ServerManager.Spawn(instance, connection, scene);
            players[connection.ClientId] = instance;
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;
            Despawn(connection.ClientId);
        }

        private void Despawn(int connectionId)
        {
            spawnGuard.Release(connectionId);
            if (!players.Remove(connectionId, out var player) || player == null)
                return;
            if (networkManager.IsServerStarted && player.IsSpawned)
                networkManager.ServerManager.Despawn(player);
            else
                Destroy(player.gameObject);
        }

        private static bool IsPlayerScene(string sceneName) =>
            sceneName == SessionController.TestScene || sceneName == SessionController.IngameScene;

        private void OnDestroy()
        {
            if (networkManager == null)
                return;
            if (networkManager.SceneManager != null)
                networkManager.SceneManager.OnClientPresenceChangeEnd -= OnClientPresenceChanged;
            if (networkManager.ServerManager != null)
                networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            spawnGuard.Clear();
            players.Clear();
        }
    }
}
