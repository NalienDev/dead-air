using Dissonance.Networking;
using Dissonance.Integrations.PurrNet;
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
        // the client might already be connected. If so, Dissonance misses the
        // connection event. We find it and start it if we are currently connected.
        var nm = NetworkManager.main;
        if (nm == null) return;

        bool isConnected = nm.isHost || nm.isClient || nm.isServer;
        if (!isConnected) return;

        var commsNetwork = Object.FindFirstObjectByType<PurrNetCommsNetwork>();
        if (commsNetwork == null) return;

        // Only kick it when it's actually down. Restarting a live comms network forces
        // a full re-handshake for this client on EVERY scene load — after enough
        // GameOver→City round-trips one of those restarts can lose the race against
        // the scene change and leave a player permanently without voice.
        if (commsNetwork.Mode != NetworkMode.None)
        {
            PurrLogger.Log("[DissonanceAutoStarter] Dissonance already running — leaving it alone.");
            return;
        }

        PurrLogger.Log("[DissonanceAutoStarter] Network is active but Dissonance is stopped. Starting it.");
        commsNetwork.TryRunManually();
    }
}
