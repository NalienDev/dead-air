using PurrNet;
using PurrNet.Logging;
using UnityEngine;

/// <summary>
/// Sits on the PlayerCapsule alongside PlayerManager.
/// Watches health and oxygen SyncVars and triggers death / revival.
/// 
/// Death conditions (either one):
///   • currentHealth reaches 0
///   • currentOxygen reaches 0
/// 
/// All state is server-authoritative via SyncVar.
/// Owner-side components (FPC, VoiceRecorder) are toggled locally when
/// the SyncVar replicates down.
/// </summary>
[RequireComponent(typeof(PlayerManager))]
public class PlayerDeathHandler : NetworkIdentity
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Components to disable on death (auto-found if empty)")]
    [SerializeField] private StarterAssets.FirstPersonController _fpc;
    [SerializeField] private VoiceRecorder _voiceRecorder;
    [SerializeField] private CharacterController _characterController;

    [Header("Visual root to hide on death (e.g. Astronaut.001)")]
    [SerializeField] private GameObject _playerVisualRoot;

    // ── Synced state ───────────────────────────────────────────────────────

    /// <summary>Replicated to all clients. True = this player is dead.</summary>
    public SyncVar<bool> isDead = new(false);

    // ── Private ────────────────────────────────────────────────────────────

    private PlayerManager _playerManager;
    private bool _lastDeadState;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _playerManager = GetComponent<PlayerManager>();

        if (_fpc == null) _fpc = GetComponent<StarterAssets.FirstPersonController>();
        if (_voiceRecorder == null) _voiceRecorder = GetComponent<VoiceRecorder>();
        if (_characterController == null) _characterController = GetComponent<CharacterController>();
    }

    protected override void OnSpawned(bool asServer)
    {
        // Subscribe to SyncVar changes so every client reacts immediately
        isDead.onChanged += OnDeadStateChanged;

        // Register into the registry so SpectatorController can find us
        if (PlayerRegistry.Instance != null)
            PlayerRegistry.Instance.Register(_playerManager);
    }

    protected override void OnDespawned(bool asServer)
    {
        isDead.onChanged -= OnDeadStateChanged;

        if (PlayerRegistry.Instance != null)
            PlayerRegistry.Instance.Unregister(_playerManager);
    }

    private void Update()
    {
        // Only the server evaluates death conditions
        if (!isServer) return;
        if (isDead.value) return;

        bool shouldDie = _playerManager.GetCurrentHealth() <= 0
                      || _playerManager.GetCurrentOxygen() <= 0;

        if (shouldDie)
            Die();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Revive this player with the given health and oxygen values.
    /// Call from any context — routes through ServerRpc.
    /// </summary>
    public void Revive(int restoreHealth, int restoreOxygen)
    {
        ServerRevive(restoreHealth, restoreOxygen);
    }

    // ── Server-side logic ──────────────────────────────────────────────────

    private void Die()
    {
        // Guard: server only, not already dead
        if (!isServer || isDead.value) return;

        PurrLogger.Log($"[PlayerDeathHandler] Player {owner} died.");

        // Zero out stats so UI is clean
        _playerManager.currentHealth.value = 0;
        _playerManager.currentOxygen.value = 0;

        isDead.value = true; // Replicates to all clients via SyncVar
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerRevive(int restoreHealth, int restoreOxygen)
    {
        if (!isDead.value) return;

        PurrLogger.Log($"[PlayerDeathHandler] Player {owner} revived.");

        _playerManager.currentHealth.value = Mathf.Clamp(restoreHealth, 1, _playerManager.maxHealth.value);
        _playerManager.currentOxygen.value = Mathf.Clamp(restoreOxygen, 1, _playerManager.maxOxygen.value);

        isDead.value = false; // Replicates — all clients react
    }

    // ── SyncVar callback (runs on ALL clients + server) ────────────────────

    private void OnDeadStateChanged(bool newValue)
    {
        if (newValue)
            ApplyDeadState();
        else
            ApplyAliveState();
    }

    private void ApplyDeadState()
    {
        // Disable movement & voice only for the owner — other clients don't have these active anyway
        if (isOwner)
        {
            if (_fpc != null) _fpc.enabled = false;
            if (_voiceRecorder != null) _voiceRecorder.enabled = false;
            if (_characterController != null) _characterController.enabled = false;
        }

        // Hide the visual mesh for everyone (so other players see a "ghost" / nothing)
        if (_playerVisualRoot != null)
            _playerVisualRoot.SetActive(false);

        // Notify UI (owner only)
        if (isOwner)
            LocalPlayerUI.Instance?.OnPlayerDied();
    }

    private void ApplyAliveState()
    {
        // Re-enable movement & voice for owner
        if (isOwner)
        {
            if (_fpc != null) _fpc.enabled = true;
            if (_voiceRecorder != null) _voiceRecorder.enabled = true;
            if (_characterController != null) _characterController.enabled = true;
        }

        // Restore visuals for everyone
        if (_playerVisualRoot != null)
            _playerVisualRoot.SetActive(true);

        // Notify UI
        if (isOwner)
            LocalPlayerUI.Instance?.OnPlayerRevived();
    }
}