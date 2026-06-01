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
    }

    [ObserversRpc(runLocally: true)]
    public void RpcShowLoadingScreen()
    {
        if (LoadingScreenManager.Instance != null)
            LoadingScreenManager.Instance.ShowLoadingScreen();
    }

    [ObserversRpc(runLocally: true)]
    public void RpcHideLoadingScreen()
    {
        if (LoadingScreenManager.Instance != null)
            LoadingScreenManager.Instance.HideLoadingScreen();
    }

    [ServerRpc(requireOwnership: false)]
    public void LoadSceneForEveryone(string sceneName)
    {
        RpcShowLoadingScreen();
        _networkManager.sceneModule.LoadSceneAsync(sceneName);
    }

    [ServerRpc(requireOwnership: false)]
    public void LoadNextSceneForEveryone()
    {
        RpcShowLoadingScreen();
        int next = SceneManager.GetActiveScene().buildIndex + 1;
        if (next < SceneManager.sceneCountInBuildSettings)
            _networkManager.sceneModule.LoadSceneAsync(next.ToString());
    }

    public void LoadSceneLocal(string sceneName)
    {
        if (LoadingScreenManager.Instance != null)
            LoadingScreenManager.Instance.ShowLoadingScreen();
            
        SceneManager.LoadScene(sceneName);
        Time.timeScale = 1f;
        PauseMenu.GameIsPaused = false;
        EventManager.OnSceneChange(sceneName);
    }
}