using PurrNet;
using PurrNet.Logging;
using UnityEngine;

/// <summary>
/// Server-authoritative death and revival for a player, hiding the body and switching the owner to spectating.
/// </summary>
[RequireComponent(typeof(PlayerManager))]
public class PlayerDeathHandler : NetworkBehaviour
{
    [Header("Components to disable on death")]
    [SerializeField] private StarterAssets.FirstPersonController _fpc;
    [SerializeField] private VoiceRecorder _voiceRecorder;
    [SerializeField] private CharacterController _characterController;

    [Header("Visual roots to hide on death")]
    [SerializeField] private GameObject[] _playerVisualRoots;

    [Header("Colliders to disable on death")]
    [Tooltip("Colliders turned off while dead so the corpse can't block or shove anyone.")]
    [SerializeField] private Collider[] _collidersToDisable;

    [Header("Death FX")]
    [Tooltip("Spawned at the death spot as its own object so it outlives the hidden body.")]
    [SerializeField] private GameObject _deathEffectPrefab;
    [Tooltip("Seconds before the spawned death-FX object destroys itself.")]
    [SerializeField] private float _deathEffectLifetime = 5f;

    [Header("Team Wipe")]
    [Tooltip("Scene loaded for everyone when the last living player dies.")]
    [SerializeField] private string _gameOverSceneName = "GameOver";

    // Replicated to all clients; true when this player is dead.
    public SyncVar<bool> isDead = new(false);

    private PlayerManager _playerManager;
    private SpectatorController _spectator;

    private void Awake()
    {
        _playerManager = GetComponent<PlayerManager>();

        if (_fpc == null) _fpc = GetComponent<StarterAssets.FirstPersonController>();
        if (_voiceRecorder == null) _voiceRecorder = GetComponent<VoiceRecorder>();
        if (_characterController == null) _characterController = GetComponent<CharacterController>();

        if (_collidersToDisable == null || _collidersToDisable.Length == 0)
            _collidersToDisable = GetComponentsInChildren<Collider>(includeInactive: true);
    }

    protected override void OnSpawned(bool asServer)
    {
        isDead.onChanged += OnDeadStateChanged;

        // Late-join safety: if already dead on spawn, hide them now, without replaying FX.
        if (isDead.value)
            ApplyDeadState(playEffects: false);
    }

    protected override void OnDespawned(bool asServer)
    {
        isDead.onChanged -= OnDeadStateChanged;
    }

    // Death check called by PlayerManager after server-side health/oxygen changes. Driven
    // from there because this component's Update is disabled on the server's copy of remotes.
    public void ServerCheckDeath()
    {
        if (!isServer || isDead.value) return;

        if (_playerManager == null) _playerManager = GetComponent<PlayerManager>();

        // Death is health-only; running out of oxygen just suffocates, draining health.
        if (_playerManager.GetCurrentHealth() <= 0)
            Die();
    }

    // Revives this player; safe to call from any context.
    public void Revive(int restoreHealth, int restoreOxygen)
    {
        ServerRevive(restoreHealth, restoreOxygen);
    }

    private void Die()
    {
        if (!isServer || isDead.value) return;

        PurrLogger.Log($"[PlayerDeathHandler] Player {owner} died.");

        _playerManager.currentHealth.value = 0;
        _playerManager.currentOxygen.value = 0;

        isDead.value = true;

        CheckTeamWipe();
    }

    // If this death left nobody alive, send everyone to the game-over scene.
    private void CheckTeamWipe()
    {
        foreach (PlayerManager pm in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
            if (!pm.IsDead) return;

        PurrLogger.Log("[PlayerDeathHandler] Every player is dead, Game Over.");

        if (QuotaManager.Instance != null)
            QuotaManager.Instance.lastGameOverReason.value = GameOverReason.TeamWiped;

        if (SceneChanger.Instance != null)
            SceneChanger.Instance.LoadSceneForEveryone(_gameOverSceneName);
        else
            Debug.LogError("[PlayerDeathHandler] SceneChanger.Instance is null, cannot load Game Over.");
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerRevive(int restoreHealth, int restoreOxygen)
    {
        if (!isDead.value) return;

        PurrLogger.Log($"[PlayerDeathHandler] Player {owner} revived.");

        _playerManager.currentHealth.value = Mathf.Clamp(restoreHealth, 1, _playerManager.maxHealth.value);
        _playerManager.currentOxygen.value = Mathf.Clamp(restoreOxygen, 1, _playerManager.maxOxygen.value);

        isDead.value = false;

        GameObject reviveLoc = GameObject.FindGameObjectWithTag("ReviveLocation");
        if (reviveLoc == null)
        {
            Debug.LogError("[PlayerDeathHandler] 'ReviveLocation' tag is not set.");
            return;
        }

        // Route through TeleportToPosition so the owner-authoritative NetworkTransform
        // doesn't overwrite a server-side move; it also toggles the CharacterController.
        _playerManager.TeleportToPosition(reviveLoc.transform.position, reviveLoc.transform.rotation);
    }

    private void OnDeadStateChanged(bool newValue)
    {
        if (newValue)
            ApplyDeadState(playEffects: true);
        else
            ApplyAliveState();
    }

    private void ApplyDeadState(bool playEffects)
    {
        SetVisualRootsActive(false);

        // No collision while dead so the corpse can't block, trip, or shove anyone.
        SetCollidersActive(false);

        if (playEffects) SpawnDeathEffect();

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
        SetCollidersActive(true);

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

    private void SetCollidersActive(bool active)
    {
        if (_collidersToDisable == null) return;
        foreach (Collider c in _collidersToDisable)
            if (c != null) c.enabled = active;
    }

    private void SpawnDeathEffect()
    {
        if (_deathEffectPrefab == null) return;

        // Not parented to the player, so hiding or teleporting the body doesn't cut off the FX.
        GameObject fx = Instantiate(_deathEffectPrefab, transform.position, transform.rotation);
        if (_deathEffectLifetime > 0f) Destroy(fx, _deathEffectLifetime);
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
