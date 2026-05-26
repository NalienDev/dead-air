using PurrNet;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrLobby
{
    public class SceneChangeButton : NetworkBehaviour
    {
        [PurrScene, SerializeField] private string scene;
        private NetworkManager _networkManager;

        private void Start()
        {
            _networkManager = FindFirstObjectByType<NetworkManager>();
        }

        public void ChangeScene()
        {
            if (_networkManager != null)
            {
                _networkManager.sceneModule.LoadSceneAsync(scene);
            }
            else
            {
                Debug.LogError("No NetworkManager Object in scene!");
            }
           
        }
    }
}
