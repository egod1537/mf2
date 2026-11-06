using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MF2.Game.Networking
{
    public enum SessionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Failed
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    public sealed class SessionController : MonoBehaviour
    {
        public const string OutgameScene = "Outgame";
        public const string TestScene = "Test";
        public const string IngameScene = "Ingame";

        public static SessionController Instance { get; private set; }
        public event Action Changed;

        public SessionStatus Status { get; private set; } = SessionStatus.Disconnected;
        public string StatusDetail { get; private set; } = string.Empty;
        public bool IsBusy { get; private set; }
        public bool IsDedicated => NetworkBootstrap.Instance != null && NetworkBootstrap.Instance.IsDedicated;
        public ushort DefaultPort => transport == null ? (ushort)7770 : transport.GetPort();
        public bool IsServer => networkManager != null && networkManager.IsServerStarted;
        public bool IsClient => networkManager != null && networkManager.IsClientStarted;
        public bool CanStartScene => IsServer && !IsBusy;

        public string Role
        {
            get
            {
                if (IsDedicated) return "Dedicated Server";
                if (networkManager == null) return "Offline";
                if (networkManager.IsHostStarted) return "Host";
                if (networkManager.IsServerOnlyStarted) return "Server";
                if (networkManager.IsClientStarted) return "Client";
                return "Offline";
            }
        }

        public IReadOnlyList<int> ConnectionIds
        {
            get
            {
                if (networkManager == null)
                    return Array.Empty<int>();
                var source = networkManager.IsServerStarted
                    ? networkManager.ServerManager.Clients.Keys
                    : networkManager.ClientManager.Clients.Keys;
                return source.OrderBy(id => id).ToArray();
            }
        }

        private NetworkManager networkManager;
        private Transport transport;
        private Coroutine activeOperation;
        private bool callbacksRegistered;
        private bool returnToOutgameOnStop;
        private bool dedicatedSceneStarted;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            networkManager = GetComponent<NetworkManager>();
        }

        private IEnumerator Start()
        {
            yield return null;
            transport = networkManager.TransportManager.Transport;
            RegisterCallbacks();
            NotifyChanged();
        }

        public bool StartHost(string portText)
        {
            if (!EndpointValidator.TryValidate("127.0.0.1", portText, out _, out var port, out var error))
                return Fail(error);
            if (IsBusy || IsServer || IsClient)
                return Fail("이미 연결 중이거나 연결되어 있습니다.");

            activeOperation = StartCoroutine(StartHostRoutine(port));
            return true;
        }

        public bool Join(string host, string portText)
        {
            if (!EndpointValidator.TryValidate(host, portText, out var normalizedHost, out var port, out var error))
                return Fail(error);
            if (IsBusy || IsServer || IsClient)
                return Fail("이미 연결 중이거나 연결되어 있습니다.");

            IsBusy = true;
            Status = SessionStatus.Connecting;
            StatusDetail = $"{normalizedHost}:{port} 연결 중";
            returnToOutgameOnStop = false;
            transport.SetClientAddress(normalizedHost);
            transport.SetPort(port);
            if (!networkManager.ClientManager.StartConnection(normalizedHost, port))
            {
                IsBusy = false;
                return Fail("클라이언트 연결 시작에 실패했습니다.");
            }

            activeOperation = StartCoroutine(ConnectionTimeoutRoutine());
            NotifyChanged();
            return true;
        }

        public void DisconnectAndReturnToOutgame()
        {
            if (activeOperation != null)
            {
                StopCoroutine(activeOperation);
                activeOperation = null;
            }

            IsBusy = true;
            returnToOutgameOnStop = true;
            if (networkManager.ClientManager.Started)
                networkManager.ClientManager.StopConnection();
            if (networkManager.ServerManager.Started)
                networkManager.ServerManager.StopConnection(true);
            StartCoroutine(ReturnToOutgameRoutine());
            NotifyChanged();
        }

        public bool StartNetworkScene(string sceneName)
        {
            if (!CanStartScene)
                return Fail("서버만 게임 씬을 시작할 수 있습니다.");
            if (sceneName != TestScene && sceneName != IngameScene)
                return Fail("허용되지 않은 네트워크 씬입니다.");

            IsBusy = true;
            StatusDetail = $"{sceneName} 로딩 중";
            var loadData = new SceneLoadData(sceneName)
            {
                ReplaceScenes = ReplaceOption.All
            };
            loadData.PreferredActiveScene = new PreferredScene(loadData.SceneLookupDatas[0]);
            networkManager.SceneManager.LoadGlobalScenes(loadData);
            StartCoroutine(ClearBusyAfterSceneLoad(sceneName));
            NotifyChanged();
            return true;
        }

        public void StartDedicatedFromCommandLine()
        {
            if (!IsDedicated || IsBusy || IsServer)
                return;

            transport ??= networkManager.TransportManager.Transport;
            if (transport == null)
            {
                Fail("Transport가 초기화되지 않았습니다.");
                return;
            }

            var port = DefaultPort;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if ((args[i].Equals("-port", StringComparison.OrdinalIgnoreCase) ||
                     args[i].Equals("-mf2Port", StringComparison.OrdinalIgnoreCase)) &&
                    ushort.TryParse(args[i + 1], out var parsed) && parsed > 0)
                    port = parsed;
            }

            IsBusy = true;
            Status = SessionStatus.Connecting;
            StatusDetail = $"Dedicated server :{port} 시작 중";
            transport.SetPort(port);
            if (!networkManager.ServerManager.StartConnection(port))
            {
                IsBusy = false;
                Fail("Dedicated server 시작에 실패했습니다.");
            }
            NotifyChanged();
        }

        private IEnumerator StartHostRoutine(ushort port)
        {
            IsBusy = true;
            Status = SessionStatus.Connecting;
            StatusDetail = $"Host :{port} 시작 중";
            transport.SetPort(port);
            transport.SetClientAddress("127.0.0.1");
            NotifyChanged();

            if (!networkManager.ServerManager.StartConnection(port))
            {
                IsBusy = false;
                Fail("서버 시작에 실패했습니다.");
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + 5f;
            while (!networkManager.ServerManager.Started && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!networkManager.ServerManager.Started || !networkManager.ClientManager.StartConnection("127.0.0.1", port))
            {
                networkManager.ServerManager.StopConnection(false);
                IsBusy = false;
                Fail("로컬 클라이언트 시작에 실패했습니다.");
                yield break;
            }

            activeOperation = StartCoroutine(ConnectionTimeoutRoutine());
        }

        private IEnumerator ConnectionTimeoutRoutine()
        {
            var deadline = Time.realtimeSinceStartup + 10f;
            while (Status == SessionStatus.Connecting && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (Status == SessionStatus.Connecting)
            {
                networkManager.ClientManager.StopConnection();
                if (networkManager.ServerManager.Started)
                    networkManager.ServerManager.StopConnection(false);
                IsBusy = false;
                Fail("연결 시간이 초과되었습니다.");
            }
            activeOperation = null;
        }

        private IEnumerator ClearBusyAfterSceneLoad(string sceneName)
        {
            var deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName).isLoaded)
                    break;
                yield return null;
            }
            IsBusy = false;
            StatusDetail = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName).isLoaded
                ? $"{sceneName}"
                : $"{sceneName} 로딩 확인 시간 초과";
            activeOperation = null;
            NotifyChanged();
        }

        private IEnumerator ReturnToOutgameRoutine()
        {
            yield return null;
            if (IsDedicated)
                yield break;

            Status = SessionStatus.Disconnected;
            StatusDetail = string.Empty;
            IsBusy = false;
            returnToOutgameOnStop = false;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != OutgameScene)
                UnityEngine.SceneManagement.SceneManager.LoadScene(OutgameScene, LoadSceneMode.Single);
            NotifyChanged();
        }

        private void RegisterCallbacks()
        {
            if (callbacksRegistered)
                return;
            callbacksRegistered = true;
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            networkManager.ClientManager.OnRemoteConnectionState += OnClientRosterChanged;
            networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            networkManager.ServerManager.OnRemoteConnectionState += OnServerRemoteConnectionState;
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    Status = SessionStatus.Connected;
                    StatusDetail = "Lobby";
                    IsBusy = false;
                    returnToOutgameOnStop = true;
                    break;
                case LocalConnectionState.Starting:
                    Status = SessionStatus.Connecting;
                    IsBusy = true;
                    break;
                case LocalConnectionState.Stopped:
                    var wasConnecting = Status == SessionStatus.Connecting;
                    IsBusy = false;
                    Status = wasConnecting ? SessionStatus.Failed : SessionStatus.Disconnected;
                    StatusDetail = wasConnecting ? "서버에 연결하지 못했습니다." : "연결이 종료되었습니다.";
                    if (returnToOutgameOnStop && !IsDedicated)
                        StartCoroutine(ReturnToOutgameRoutine());
                    break;
            }
            NotifyChanged();
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started && IsDedicated)
            {
                Status = SessionStatus.Connected;
                StatusDetail = "클라이언트 대기 중";
                IsBusy = false;
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped && !networkManager.ClientManager.Started)
            {
                Status = SessionStatus.Disconnected;
                IsBusy = false;
            }
            NotifyChanged();
        }

        private void OnServerRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            NotifyChanged();
            if (IsDedicated && !dedicatedSceneStarted && args.ConnectionState == RemoteConnectionState.Started)
                StartCoroutine(StartDedicatedTestWhenAuthenticated(connection));
        }

        private IEnumerator StartDedicatedTestWhenAuthenticated(NetworkConnection connection)
        {
            var deadline = Time.realtimeSinceStartup + 10f;
            while (connection.IsActive && !connection.IsAuthenticated && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!dedicatedSceneStarted && connection.IsActive && connection.IsAuthenticated)
            {
                dedicatedSceneStarted = true;
                StartNetworkScene(TestScene);
            }
        }

        private void OnClientRosterChanged(RemoteConnectionStateArgs args) => NotifyChanged();

        private bool Fail(string message)
        {
            Status = SessionStatus.Failed;
            StatusDetail = message;
            NotifyChanged();
            return false;
        }

        private void NotifyChanged() => Changed?.Invoke();

        private void OnDestroy()
        {
            if (callbacksRegistered && networkManager != null)
            {
                networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
                networkManager.ClientManager.OnRemoteConnectionState -= OnClientRosterChanged;
                networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
                networkManager.ServerManager.OnRemoteConnectionState -= OnServerRemoteConnectionState;
            }
            if (Instance == this)
                Instance = null;
        }
    }
}
