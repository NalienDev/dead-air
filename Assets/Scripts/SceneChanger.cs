using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChanger : NetworkIdentity
{
    public static SceneChanger Instance { get; private set; }

    [SerializeField] private NetworkManager _networkManager;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _networkManager.onServerConnectionState += OnServerState;
    }

    protected override void OnDestroy()
    {
        _networkManager.onServerConnectionState -= OnServerState;
    }

    private void OnServerState(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
            _networkManager.sceneModule.LoadSceneAsync("GameLobby");
    }

    [ServerRpc(requireOwnership: false)]
    public void LoadSceneForEveryone(string sceneName)
    {
        _networkManager.sceneModule.LoadSceneAsync(sceneName);
    }

    [ServerRpc(requireOwnership: false)]
    public void LoadNextSceneForEveryone()
    {
        int next = SceneManager.GetActiveScene().buildIndex + 1;
        if (next < SceneManager.sceneCountInBuildSettings)
            _networkManager.sceneModule.LoadSceneAsync(next.ToString());
    }

    public void LoadSceneLocal(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
        Time.timeScale = 1f;
        PauseMenu.GameIsPaused = false;
        EventManager.OnSceneChange(sceneName);
    }
}