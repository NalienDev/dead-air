using TMPro;
using UnityEngine;

/// <summary>
/// Extends the original LocalPlayerUI with death and spectator states.
/// 
/// New required UI references (add these to your PlayerUI Canvas):
///   • _spectatorPanel       — a panel shown only while spectating
///   • _spectatorTargetText  — "Spectating: [PlayerName]" label
///   • _spectatorHintText    — "← → to switch player" hint
///   • _deadOverlay          — full-screen tint/overlay shown on death
/// 
/// Existing references (_healthText, _oxygenText) continue to work as before.
/// </summary>
public class LocalPlayerUI : MonoBehaviour
{
    public static LocalPlayerUI Instance { get; private set; }

    // ── Existing refs ──────────────────────────────────────────────────────
    [Header("Vitals HUD")]
    [SerializeField] private TextMeshProUGUI _healthText;
    [SerializeField] private TextMeshProUGUI _oxygenText;

    // ── New: death / spectator UI ──────────────────────────────────────────
    [Header("Death Overlay")]
    [Tooltip("Full-screen panel shown the instant the local player dies.")]
    [SerializeField] private GameObject _deadOverlay;

    [Header("Spectator HUD")]
    [Tooltip("Panel that wraps all spectator-related elements.")]
    [SerializeField] private GameObject _spectatorPanel;

    [Tooltip("Label showing who is being spectated.")]
    [SerializeField] private TextMeshProUGUI _spectatorTargetText;

    [Tooltip("Key-hint label e.g. '← → to switch player'.")]
    [SerializeField] private TextMeshProUGUI _spectatorHintText;

    // ── Private ────────────────────────────────────────────────────────────

    private bool _isDead;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        // Start with spectator / dead UI hidden
        SetDeadOverlayVisible(false);
        SetSpectatorPanelVisible(false);
    }

    private void Update()
    {
        if (_isDead) return;                         // don't poll vitals while dead
        if (PlayerManager.Local == null) return;

        _healthText.text = $"Health: {PlayerManager.Local.GetCurrentHealth()} / {PlayerManager.Local.GetMaxHealth()}";
        _oxygenText.text = $"Oxygen: {PlayerManager.Local.GetCurrentOxygen()} / {PlayerManager.Local.GetMaxOxygen()}";
    }

    // ── Called by PlayerDeathHandler ──────────────────────────────────────

    /// <summary>Switches the HUD into dead / spectator mode.</summary>
    public void OnPlayerDied()
    {
        _isDead = true;
        SetDeadOverlayVisible(true);
        SetSpectatorPanelVisible(true);

        if (_spectatorTargetText != null)
            _spectatorTargetText.text = "Waiting for a player to spectate…";

        if (_spectatorHintText != null)
            _spectatorHintText.text = "← → Switch player";
    }

    /// <summary>Restores the HUD to the normal alive state.</summary>
    public void OnPlayerRevived()
    {
        _isDead = false;
        SetDeadOverlayVisible(false);
        SetSpectatorPanelVisible(false);
    }

    // ── Called by SpectatorController ─────────────────────────────────────

    /// <summary>Updates the spectator label when the camera switches target.</summary>
    public void OnSpectatorTargetChanged(PlayerManager target)
    {
        if (_spectatorTargetText == null) return;

        string displayName = target != null ? target.name : "Nobody";
        _spectatorTargetText.text = $"Spectating: {displayName}";
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetDeadOverlayVisible(bool visible)
    {
        if (_deadOverlay != null)
            _deadOverlay.SetActive(visible);
    }

    private void SetSpectatorPanelVisible(bool visible)
    {
        if (_spectatorPanel != null)
            _spectatorPanel.SetActive(visible);
    }
}