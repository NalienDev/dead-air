using PurrNet;
using UnityEngine;

/// <summary>
/// Loads the first scene on the server once the network spawns.
/// </summary>
public class BootstrapLoader : NetworkBehaviour
{
    [SerializeField] private NetworkManager _networkManager;
    [SerializeField] private string firstScene = "GameLobby";

    protected override void OnSpawned(bool asServer)
    {
        if (!asServer) return;

        if (LoadingScreenManager.Instance != null)
            LoadingScreenManager.Instance.ShowLoadingScreen();

        _networkManager.sceneModule.LoadSceneAsync(firstScene);
    }
}
