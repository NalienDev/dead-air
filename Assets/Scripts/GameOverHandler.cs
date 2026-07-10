using PurrNet;
using UnityEngine;
using System.Collections;

/// <summary>
/// Lives in the GameOver scene. After a short delay it resets the run (day 1, quota,
/// bandwidth, energy) and loads everyone back into the City.
///
/// IMPORTANT: only the SERVER/HOST runs the reset + scene load. This script exists on
/// every client (each loads the GameOver scene), and both calls route through
/// ServerRpcs (requireOwnership: false) — so without the guard, N players meant the
/// City scene got loaded N times back-to-back. Those pile-up reloads restarted
/// Dissonance repeatedly and could drop a client's voice connection for good.
/// </summary>
public class GameOverHandler : MonoBehaviour
{
    [SerializeField] private float _delayBeforeReset = 5f;
    [SerializeField] private string _mainSceneName = "Main";

#if UNITY_EDITOR
    [Header("Editor Testing")]
    [Tooltip("When you Play this scene alone in the Editor, there's no host/NetworkManager running, so the isServer check below would silently skip the scene change. With this on, that check is bypassed in the Editor only - never compiled into an actual build.")]
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
