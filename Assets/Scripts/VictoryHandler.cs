using PurrNet;
using UnityEngine;
using System.Collections;

/// <summary>
/// Resets the run and returns everyone to the lobby after a short victory delay.
/// </summary>
public class VictoryHandler : MonoBehaviour
{
    [SerializeField] private float _delayBeforeReturn = 5f;
    [SerializeField] private string _lobbySceneName = "GameLobby";

#if UNITY_EDITOR
    [Header("Editor Testing")]
    [Tooltip("Bypass the isServer check so the scene change runs when playing this scene alone in the Editor.")]
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
            QuotaManager.Instance.ServerResetGame();

        if (SceneChanger.Instance != null)
            SceneChanger.Instance.LoadSceneForEveryone(_lobbySceneName);
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(_lobbySceneName);
    }
}
