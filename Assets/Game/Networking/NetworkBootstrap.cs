using System.Collections;
using FishNet.Managing;
using FishNet.Managing.Timing;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MF2.Game.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(SessionController), typeof(PlayerSpawner))]
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        public static NetworkBootstrap Instance { get; private set; }

        [SerializeField, Range(10, 120)] private ushort tickRate = 30;

        public bool IsDedicated { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            IsDedicated = DetectDedicated();
        }

        private IEnumerator Start()
        {
            yield return null;

            var networkManager = GetComponent<NetworkManager>();
            networkManager.TimeManager.SetTickRate(tickRate);
            networkManager.TimeManager.SetPhysicsMode(PhysicsMode.TimeManager);

            if (IsDedicated)
            {
                DisablePresentationObjects();
                GetComponent<SessionController>().StartDedicatedFromCommandLine();
            }
        }

        private static bool DetectDedicated()
        {
#if UNITY_SERVER
            return true;
#else
            return Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
#endif
        }

        private static void DisablePresentationObjects()
        {
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                canvas.gameObject.SetActive(false);
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                camera.gameObject.SetActive(false);
            foreach (var eventSystem in FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                eventSystem.gameObject.SetActive(false);
            AudioListener.pause = true;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
