using System.Linq;
using MF2.Game.Networking;
using UnityEngine;

namespace MF2.Game.UI
{
    public sealed class NetworkSceneHud : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Text statusText;
        [SerializeField] private UnityEngine.UI.Button disconnectButton;

        private SessionController session;

        private void Start()
        {
            session = SessionController.Instance;
            disconnectButton.onClick.AddListener(Disconnect);
        }

        private void Update()
        {
            session ??= SessionController.Instance;
            if (session == null)
            {
                statusText.text = "Network unavailable";
                return;
            }

            var ids = session.ConnectionIds;
            statusText.text = $"{session.Role} | {session.Status}\nPlayers: {ids.Count} [{string.Join(", ", ids)}]";
        }

        private void Disconnect() => session?.DisconnectAndReturnToOutgame();
    }
}
