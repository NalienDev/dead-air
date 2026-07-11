using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent singleton that shows and hides the loading screen across scene loads.
/// </summary>
public class LoadingScreenManager : MonoBehaviour
{
    public static LoadingScreenManager Instance { get; private set; }

    [SerializeField] private GameObject _loadingPanel;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Main and Bootstrap hide the screen themselves once the player spawns.
        if (scene.name != "Main" && scene.name != "Bootstrap")
        {
            HideLoadingScreen();
        }
    }

    public void ShowLoadingScreen()
    {
        _loadingPanel.SetActive(true);
    }

    public void HideLoadingScreen()
    {
        _loadingPanel.SetActive(false);
    }
}
