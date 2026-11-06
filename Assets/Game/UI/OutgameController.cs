using System.Linq;
using MF2.Game.Networking;
using UnityEngine;

namespace MF2.Game.UI
{
    public sealed class OutgameController : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.InputField hostInput;
        [SerializeField] private UnityEngine.UI.InputField portInput;
        [SerializeField] private UnityEngine.UI.Text statusText;
        [SerializeField] private UnityEngine.UI.Text rosterText;
        [SerializeField] private UnityEngine.UI.Button hostButton;
        [SerializeField] private UnityEngine.UI.Button joinButton;
        [SerializeField] private UnityEngine.UI.Button disconnectButton;
        [SerializeField] private UnityEngine.UI.Button startTestButton;
        [SerializeField] private UnityEngine.UI.Button startIngameButton;

        private SessionController session;

        private void Start()
        {
            session = SessionController.Instance;
            hostInput.text = "127.0.0.1";
            portInput.text = session == null ? "7770" : session.DefaultPort.ToString();
            if (session != null)
                session.Changed += Refresh;
            Refresh();
        }

        public void Host() => session?.StartHost(portInput.text);
        public void Join() => session?.Join(hostInput.text, portInput.text);
        public void Disconnect() => session?.DisconnectAndReturnToOutgame();
        public void StartTest() => session?.StartNetworkScene(SessionController.TestScene);
        public void StartIngame() => session?.StartNetworkScene(SessionController.IngameScene);

        private void Update()
        {
            if (session == null && SessionController.Instance != null)
            {
                session = SessionController.Instance;
                session.Changed += Refresh;
            }
            Refresh();
        }

        private void Refresh()
        {
            if (session == null)
            {
                statusText.text = "Disconnected - Network bootstrap 없음";
                return;
            }

            statusText.text = $"{session.Status} | {session.Role}\n{session.StatusDetail}";
            var ids = session.ConnectionIds;
            rosterText.text = $"Players: {ids.Count}\nConnection IDs: {(ids.Count == 0 ? "-" : string.Join(", ", ids))}";
            var offline = !session.IsServer && !session.IsClient;
            hostButton.interactable = offline && !session.IsBusy;
            joinButton.interactable = offline && !session.IsBusy;
            disconnectButton.interactable = !offline || session.IsBusy;
            startTestButton.gameObject.SetActive(session.IsServer);
            startIngameButton.gameObject.SetActive(session.IsServer);
            startTestButton.interactable = session.CanStartScene;
            startIngameButton.interactable = session.CanStartScene;
        }

        private void OnDestroy()
        {
            if (session != null)
                session.Changed -= Refresh;
        }
    }
}
