using PurrNet;
using UnityEngine;
using System.Collections;

/// <summary>
/// Lives in the Victory scene (a copy of GameOver, minus the Reason sign).
/// After a short delay, resets the run (day 1, quota, bandwidth, energy) and
/// loads everyone back into the Lobby - same structure as GameOverHandler,
/// just returning to GameLobby instead of the City, since winning doesn't
/// need to restart the actual run/dungeon.
///
/// IMPORTANT: only the SERVER/HOST runs the reset + scene load, same guard
/// as GameOverHandler - every client loads this scene, and without the
/// isServer check N players would each trigger their own scene load.
/// </summary>
public class VictoryHandler : MonoBehaviour
{
    [SerializeField] private float _delayBeforeReturn = 5f;
    [SerializeField] private string _lobbySceneName = "GameLobby";

#if UNITY_EDITOR
    [Header("Editor Testing")]
    [Tooltip("When you Play this scene alone in the Editor, there's no host/NetworkManager running, so the isServer check below would silently skip the scene change. With this on, that check is bypassed in the Editor only - never compiled into an actual build.")]
    [SerializeField] private bool forceReturnWithoutServer = true;
#endif

    private void Start()
    {
        StartCoroutine(ReturnSequence());
    }

    private IEnumerator ReturnSequence()
    {
        yield return new WaitForSeconds(_delayBeforeReturn);

        if (LoadingScreenManager.Instance != null)
            LoadingScreenManager.Instance.ShowLoadingScreen();

        // Only the server drives the reset — clients just show their loading screen
        // and wait for the networked scene change to arrive.
        NetworkManager nm = NetworkManager.main;
        bool proceed = nm != null && nm.isServer;

#if UNITY_EDITOR
        if (!proceed && forceReturnWithoutServer)
        {
            Debug.LogWarning("[VictoryHandler] No active host/NetworkManager found - forcing the scene change anyway (Editor-only test bypass, won't happen in a build).");
            proceed = true;
        }
#endif

        if (!proceed) yield break;

        if (QuotaManager.Instance != null)
            QuotaManager.Instance.ServerResetGame(); // fresh run, back at day 1

        if (SceneChanger.Instance != null)
            SceneChanger.Instance.LoadSceneForEveryone(_lobbySceneName);
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(_lobbySceneName);
    }
}
