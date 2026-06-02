using UnityEngine;
using UnityEngine.SceneManagement;
using PurrNet;
using PurrNet.Logging;

public static class DissonanceAutoStarter
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // When a new scene loads (like transitioning from Bootstrap to City),
        // the client might already be connected. If so, Dissonance misses the connection event.
        // We find it and force it to start if we are currently connected.
        
        var nm = NetworkManager.main;
        if (nm == null) return;
        
        bool isConnected = nm.isHost || nm.isClient || nm.isServer;
        
        if (isConnected)
        {
            MonoBehaviour[] allBehaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var b in allBehaviours)
            {
                if (b.GetType().Name == "PurrNetCommsNetwork")
                {
                    PurrLogger.Log("[DissonanceAutoStarter] Network is already active. Manually starting Dissonance client.");
                    b.Invoke("TryRunManually", 0f);
                    break;
                }
            }
        }
    }
}
