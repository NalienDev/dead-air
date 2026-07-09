using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingScreenManager : MonoBehaviour
{
    public static LoadingScreenManager Instance { get; private set; }

    [SerializeField] private GameObject _loadingPanel;

    private void Awake()
    {
        // Make sure only one loading screen exists across scene loads
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
        // Listen to scene changes
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Don't hide the loading screen automatically for Bootstrap or Main.
        // For Main, it will be hidden when the player actually spawns.
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
