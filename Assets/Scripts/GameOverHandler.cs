using PurrNet;
using UnityEngine;
using System.Collections;

/// <summary>
/// Resets the run and loads everyone back into the City after a short game-over delay.
/// </summary>
public class GameOverHandler : MonoBehaviour
{
    [SerializeField] private float _delayBeforeReset = 5f;
    [SerializeField] private string _mainSceneName = "Main";

#if UNITY_EDITOR
    [Header("Editor Testing")]
    [Tooltip("Bypass the isServer check so the scene change runs when playing this scene alone in the Editor.")]
    [SerializeField] private bool forceResetWithoutServer = true;
#endif

    private void Start()
    {
        StartCoroutine(ResetSequence());
    }

    private IEnumerator ResetSequence()
    {
        yield return new WaitForSeconds(_delayBeforeReset);

        if (LoadingScreenManager.Instance != null)
            LoadingScreenManager.Instance.ShowLoadingScreen();

        // Only the server drives the reset — clients just show their loading screen
        // and wait for the networked scene change to arrive.
        NetworkManager nm = NetworkManager.main;
        bool proceed = nm != null && nm.isServer;

#if UNITY_EDITOR
        if (!proceed && forceResetWithoutServer)
        {
            Debug.LogWarning("[GameOverHandler] No active host/NetworkManager found - forcing the scene change anyway (Editor-only test bypass, won't happen in a build).");
            proceed = true;
        }
#endif

        if (!proceed) yield break;

        if (QuotaManager.Instance != null)
            QuotaManager.Instance.ServerResetGame(); // back to day 1

        if (SceneChanger.Instance != null)
            SceneChanger.Instance.LoadSceneForEveryone(_mainSceneName);
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(_mainSceneName);
    }
}
