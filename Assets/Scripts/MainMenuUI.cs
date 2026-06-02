using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Steam-free main menu. Stores the player's intent via ConnectionIntent,
/// then does a plain SceneManager load to BootstrapScene where the
/// NetworkManager lives and will handle starting host / client.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Scene")]
    [Tooltip("Exact name of your Bootstrap scene as it appears in Build Settings.")]
    [SerializeField] private string _bootstrapSceneName = "BootstrapScene";

    [Header("UI References")]
    [SerializeField] private TMP_InputField _roomCodeInput;
    [SerializeField] private TextMeshProUGUI _statusText;

    // ── Button callbacks ──────────────────────────────────────────────

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

    // ── Helpers ───────────────────────────────────────────────────────

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