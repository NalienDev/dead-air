using PurrNet;
using UnityEngine;

/// <summary>
/// Stays as a NetworkBehaviour so OnSpawned fires server-side
/// and loads the first scene — same as before.
/// Transport/room-code configuration is handled by BootstrapConnector (separate MonoBehaviour).
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
