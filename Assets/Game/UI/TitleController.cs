using MF2.Game.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MF2.Game.UI
{
    public sealed class TitleController : MonoBehaviour
    {
        public void StartGame()
        {
            if (NetworkBootstrap.Instance != null && NetworkBootstrap.Instance.IsDedicated)
                return;
            SceneManager.LoadScene(SessionController.OutgameScene, LoadSceneMode.Single);
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
