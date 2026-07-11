using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Main menu that records the player's host/join intent and loads the bootstrap scene.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Scene")]
    [Tooltip("Name of the Bootstrap scene as it appears in Build Settings.")]
    [SerializeField] private string _bootstrapSceneName = "BootstrapScene";

    [Header("UI References")]
    [SerializeField] private TMP_InputField _roomCodeInput;
    [SerializeField] private TextMeshProUGUI _statusText;

    public void OnHostClicked()
    {
        SetStatus("Starting as host...");
        ConnectionIntent.SetHost();
        LoadBootstrap();
    }

    public void OnJoinClicked()
    {
        string code = _roomCodeInput != null ? _roomCodeInput.text.Trim() : string.Empty;

        if (string.IsNullOrEmpty(code))
        {
            SetStatus("Enter a room code first.");
            return;
        }

        SetStatus($"Joining room {code.ToUpper()}...");
        ConnectionIntent.SetJoin(code);
        LoadBootstrap();
    }

    private void LoadBootstrap()
    {
        SceneManager.LoadScene(_bootstrapSceneName);
    }

    private void SetStatus(string message)
    {
        if (_statusText != null)
            _statusText.text = message;

        Debug.Log($"[MainMenuUI] {message}");
    }
}