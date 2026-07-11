using Dissonance.Networking;
using Dissonance.Integrations.PurrNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using PurrNet;
using PurrNet.Logging;

/// <summary>
/// Starts Dissonance after a scene load when the network is already connected.
/// </summary>
public static class DissonanceAutoStarter
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // On a scene load the client may already be connected, so Dissonance misses the
        // connection event; start it manually if so.
        var nm = NetworkManager.main;
        if (nm == null) return;

        bool isConnected = nm.isHost || nm.isClient || nm.isServer;
        if (!isConnected) return;

        var commsNetwork = Object.FindFirstObjectByType<PurrNetCommsNetwork>();
        if (commsNetwork == null) return;

        // Only start it when it's actually down; restarting a live comms network forces a
        // full re-handshake that can race the scene change and drop a player's voice.
        if (commsNetwork.Mode != NetworkMode.None)
        {
            PurrLogger.Log("[DissonanceAutoStarter] Dissonance already running, leaving it alone.");
            return;
        }

        PurrLogger.Log("[DissonanceAutoStarter] Network is active but Dissonance is stopped. Starting it.");
        commsNetwork.TryRunManually();
    }
}
