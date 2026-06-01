using PurrNet;
using UnityEngine;

public class BootstrapLoader : NetworkBehaviour
{
    [SerializeField] private NetworkManager _networkManager;
    [SerializeField] private string firstScene = "GameLobby";

    protected override void OnSpawned(bool asServer)
    {
        if (LoadingScreenManager.Instance != null)
            LoadingScreenManager.Instance.ShowLoadingScreen();
            
        _networkManager.sceneModule.LoadSceneAsync(firstScene);
    }
}
