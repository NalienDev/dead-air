using PurrNet;
using PurrNet.Transports;
using UnityEngine;

/// <summary>
/// Place this on the same GameObject as the NetworkManager in BootstrapScene.
///
/// Runs in Awake (before ConnectionStarter.Start) to:
///   1. Read ConnectionIntent left by the MainMenu.
///   2. Configure the PurrTransport room name.
///   3. Start the NetworkManager as Host or Client.
///
/// If no ConnectionIntent is found (e.g. you pressed Play directly in
/// BootstrapScene from the Editor), it defaults to Host so dev iteration works.
///
/// ConnectionStarter from PurrLobby is harmless here — it looks for a
/// LobbyDataHolder and logs an error then exits when none is found.
/// You do NOT need to remove ConnectionStarter.
/// </summary>
public class BootstrapConnector : MonoBehaviour
{
    [SerializeField] private NetworkManager _networkManager;
    [SerializeField] private PurrTransport _transport;

    [Tooltip("Delay in seconds before StartClient is called, to give the server time to be ready.")]
    [SerializeField] private float _clientStartDelay = 1f;

    private void Awake()
    {
        // If we entered via a PurrLobby, let ConnectionStarter handle the networking
        if (FindFirstObjectByType<PurrLobby.LobbyDataHolder>() != null)
        {
            Debug.Log("[BootstrapConnector] PurrLobby LobbyDataHolder found. Yielding to ConnectionStarter.");
            return;
        }

        var intent = ConnectionIntent.Instance;

        if (intent == null || intent.CurrentIntent == ConnectionIntent.Intent.None)
        {
            Debug.Log("[BootstrapConnector] No ConnectionIntent — defaulting to Host.");
            ApplyHost();
        }
        else if (intent.CurrentIntent == ConnectionIntent.Intent.Host)
        {
            Debug.Log("[BootstrapConnector] Intent = Host.");
            ApplyHost();
        }
        else
        {
            Debug.Log($"[BootstrapConnector] Intent = Join, room = {intent.RoomCode}");
            ApplyJoin(intent.RoomCode);
        }

        ConnectionIntent.Consume();
    }

    private void ApplyHost()
    {
        if (_transport != null)
        {
            string room = System.Guid.NewGuid().ToString()[..6].ToUpper();
            _transport.roomName = room;
            Debug.Log($"[BootstrapConnector] Generated room code: {room}");
        }

        _networkManager.StartHost();
    }

    private void ApplyJoin(string roomCode)
    {
        if (_transport != null)
            _transport.roomName = roomCode;

        // Small delay so the host is listening before the client connects
        StartCoroutine(StartClientDelayed());
    }

    private System.Collections.IEnumerator StartClientDelayed()
    {
        yield return new WaitForSeconds(_clientStartDelay);
        _networkManager.StartClient();
    }
}
