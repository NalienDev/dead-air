using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ESC opens a direct confirmation panel — "Sair para o menu principal?" with Yes/No —
/// rather than a full pause menu with several options. This is multiplayer, so nothing
/// here touches Time.timeScale — that would freeze local simulation/physics/animation
/// while every other player keeps going, desyncing this client instead of "pausing"
/// anything. Opening the panel just shows UI and frees the cursor; the world keeps
/// running underneath exactly like it does for everyone else.
///
/// Setup: put this on any always-active object in the Main scene. Build a panel with a
/// question label and two buttons, assign the panel to <see cref="_confirmPanel"/>
/// (starts hidden), wire the "Sim" button to <see cref="ConfirmQuitToMainMenu"/> and the
/// "Não" button to <see cref="CancelQuit"/>.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public static bool GameIsPaused = false;

    [Tooltip("The confirmation panel — question text + Sim/Não buttons. Starts hidden.")]
    [SerializeField] private GameObject _confirmPanel;

    [Tooltip("Scene loaded when the player confirms quitting.")]
    [SerializeField] private string _mainMenuSceneName = "SteamLobby";

    private void Awake()
    {
        if (_confirmPanel != null) _confirmPanel.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (GameIsPaused) Resume();
            else Pause();
        }
    }

    // ── Pause / resume (ESC toggles between these) ────────────────────────────

    public void Pause()
    {
        if (_confirmPanel != null) _confirmPanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        GameIsPaused = true;
    }

    public void Resume()
    {
        if (_confirmPanel != null) _confirmPanel.SetActive(false);
        GameIsPaused = false;
    }

    // ── Wire these two to the panel's buttons ─────────────────────────────────

    /// <summary>"Sim" button — actually leave to the main menu.</summary>
    public void ConfirmQuitToMainMenu()
    {
        GameIsPaused = false;
        SceneManager.LoadScene(_mainMenuSceneName);
    }

    /// <summary>"Não" button — same as resuming.</summary>
    public void CancelQuit() => Resume();

    // ── Kept for a real "quit the application" button elsewhere, if you add one ──

    public void QuitGame()
    {
        Debug.Log("Quitting Game");
        Application.Quit();
    }
}
