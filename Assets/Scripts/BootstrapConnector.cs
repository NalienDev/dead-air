using PurrNet;
using PurrNet.Transports;
using UnityEngine;

/// <summary>
/// Reads the ConnectionIntent left by the main menu and starts the NetworkManager as host or client.
/// </summary>
public class BootstrapConnector : MonoBehaviour
{
    [SerializeField] private NetworkManager _networkManager;
    [SerializeField] private PurrTransport _transport;

    [Tooltip("Delay before StartClient, to give the server time to be ready.")]
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
