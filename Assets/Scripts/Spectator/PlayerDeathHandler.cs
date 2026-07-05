using PurrNet;
using PurrNet.Logging;
using UnityEngine;

/// <summary>
/// Sits on the PlayerCapsule alongside PlayerManager.
/// Watches health and oxygen and triggers death / revival.
///
/// Extends NetworkBehaviour (not NetworkIdentity) so it shares the same network
/// context as PlayerManager on the same GameObject — a second NetworkIdentity on
/// one GameObject breaks SyncVar replication for the extra component.
///
/// Death conditions (either one):
///   • currentHealth reaches 0
///   • currentOxygen reaches 0
///
/// State is server-authoritative via the <see cref="isDead"/> SyncVar. When it
/// flips, every client hides the dead player's body; the owning client also
/// disables its own control components and drops into third-person spectator mode
/// (see <see cref="SpectatorController"/>).
/// </summary>
[RequireComponent(typeof(PlayerManager))]
public class PlayerDeathHandler : NetworkBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Components to disable on death (auto-found if empty)")]
    [SerializeField] private StarterAssets.FirstPersonController _fpc;
    [SerializeField] private VoiceRecorder _voiceRecorder;
    [SerializeField] private CharacterController _characterController;

    [Header("Visual roots to hide on death (e.g. Models). Add as many as you need.")]
    [SerializeField] private GameObject[] _playerVisualRoots;

    // ── Synced state ───────────────────────────────────────────────────────

    /// <summary>Replicated to all clients. True = this player is dead.</summary>
    public SyncVar<bool> isDead = new(false);

    // ── Private ────────────────────────────────────────────────────────────

    private PlayerManager _playerManager;
    private SpectatorController _spectator;

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
        isDead.onChanged += OnDeadStateChanged;

        // Late-join safety: if this player is already dead when we spawn in,
        // hide them right away instead of waiting for the next change event.
        if (isDead.value)
            ApplyDeadState();
    }

    protected override void OnDespawned(bool asServer)
    {
        isDead.onChanged -= OnDeadStateChanged;
    }

    /// <summary>
    /// Server-authoritative death check. Called by <see cref="PlayerManager"/>
    /// after it changes health/oxygen on the server.
    ///
    /// We can't detect death from this component's Update: NetworkOwnershipToggle
    /// disables PlayerDeathHandler on the server's copy of a remote client, so its
    /// Update never runs for them — which is why previously only the host could
    /// die. Driving it from PlayerManager's ServerRpcs (which always run on the
    /// server) makes every player die correctly.
    /// </summary>
    public void ServerCheckDeath()
    {
        if (!isServer || isDead.value) return;

        if (_playerManager == null) _playerManager = GetComponent<PlayerManager>();

        if (_playerManager.GetCurrentHealth() <= 0 || _playerManager.GetCurrentOxygen() <= 0)
            Die();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Revive this player. Safe to call from any context.</summary>
    public void Revive(int restoreHealth, int restoreOxygen)
    {
        ServerRevive(restoreHealth, restoreOxygen);
    }

    // ── Server-side logic ──────────────────────────────────────────────────

    private void Die()
    {
        if (!isServer || isDead.value) return;

        PurrLogger.Log($"[PlayerDeathHandler] Player {owner} died.");

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

        GameObject reviveLoc = GameObject.FindGameObjectWithTag("ReviveLocation");
        if (reviveLoc == null)
        {
            Debug.LogError("[PlayerDeathHandler] 'ReviveLocation' tag is not set.");
            return;
        }

        // Route through TeleportToPosition (ObserversRpc, runLocally) instead of
        // moving the transform here on the server. The NetworkTransform is
        // owner-authoritative, so a server-side move gets overwritten by the
        // owning client's replicated position — that's why only the host (who
        // owns their own player) respawned correctly. TeleportToPosition runs on
        // the owner and disables the CharacterController around the move so it sticks.
        _playerManager.TeleportToPosition(reviveLoc.transform.position, reviveLoc.transform.rotation);
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
        // Hidden for everyone — the corpse should not be visible to other players.
        SetVisualRootsActive(false);

        if (!isOwner) return;

        if (_fpc != null) _fpc.enabled = false;
        if (_voiceRecorder != null) _voiceRecorder.enabled = false;
        if (_characterController != null) _characterController.enabled = false;

        Transform camTarget = (_fpc != null && _fpc.CinemachineCameraTarget != null)
            ? _fpc.CinemachineCameraTarget.transform
            : null;

        EnsureSpectator().BeginSpectating(_playerManager, camTarget);
    }

    private void ApplyAliveState()
    {
        SetVisualRootsActive(true);

        if (!isOwner) return;

        if (_spectator != null) _spectator.EndSpectating();

        if (_fpc != null) _fpc.enabled = true;
        if (_voiceRecorder != null) _voiceRecorder.enabled = true;
        if (_characterController != null) _characterController.enabled = true;
    }

    private void SetVisualRootsActive(bool active)
    {
        if (_playerVisualRoots == null) return;
        foreach (GameObject root in _playerVisualRoots)
            if (root != null) root.SetActive(active);
    }

    private SpectatorController EnsureSpectator()
    {
        if (_spectator == null)
        {
            _spectator = GetComponent<SpectatorController>();
            if (_spectator == null)
                _spectator = gameObject.AddComponent<SpectatorController>();
        }
        return _spectator;
    }
}
