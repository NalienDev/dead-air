using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using TMPro;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private NetworkManager _networkManager;
    [SerializeField] private PurrTransport _transport;
    [SerializeField] private TMP_InputField _roomInput;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private GameObject _canvas;

    public void Host()
    {
        string room = System.Guid.NewGuid().ToString()[..6].ToUpper(); // short random code
        _transport.roomName = room;
        _networkManager.StartHost();
        _statusText.text = "Room code: " + room;
        DontDestroyOnLoad(_canvas.gameObject);
    }

    public void Join(string text)
    {
        _transport.roomName = text.ToUpper();
        _networkManager.StartClient();
    }

}