using JamesFrowen.SimpleWeb;
using PurrNet;
using PurrNet.Transports;
using System;
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
        _networkManager.onClientConnectionState += OnClientState;
    }

    private void OnSceneLoaded(SceneID scene, bool asServer)
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "GameLobby")
        {
            var bridge = FindFirstObjectByType<Dissonance.Integrations.PurrNet.PurrNetCommsNetwork>();

            if (bridge != null)
            {
                PurrNet.Logging.PurrLogger.Log($"Lobby Scene Loaded. Manually starting Dissonance. AsServer: {asServer}");

                bridge.TryRunManually();
            }
            else
            {
                PurrNet.Logging.PurrLogger.LogError("Could not find PurrNetCommsNetwork in GameLobby!");
            }
        }
    }

    protected override void OnDestroy()
    {
        _networkManager.onServerConnectionState -= OnServerState;
        _networkManager.onClientConnectionState -= OnClientState;
        _networkManager.sceneModule.onSceneLoaded -= OnSceneLoaded;

    }

    private void OnClientState(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
        {
            _networkManager.sceneModule.onSceneLoaded += OnSceneLoaded;
        }
    }


    private void OnServerState(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
        {
            _networkManager.sceneModule.LoadSceneAsync("GameLobby");
            _networkManager.sceneModule.onSceneLoaded += OnSceneLoaded;
        }
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