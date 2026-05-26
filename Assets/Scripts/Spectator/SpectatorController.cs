using System.Collections.Generic;
using PurrNet;
using PurrNet.Logging;
using UnityEngine;

/// <summary>
/// Attached to the PlayerCapsule. Activates only for the owning client when they die.
/// Moves a free-floating spectator camera through alive players.
///
/// Requirements in the Inspector:
///   • SpectatorCameraRoot  — assign the PlayerCameraRoot transform
///   • SpectatorCamera      — assign the PlayerFollowCamera (Cinemachine Vcam) GameObject
///
/// The spectator camera is re-parented to each target's camera root so Cinemachine
/// continues to work naturally.
///
/// IMPROVEMENTS:
///   - Uses PlayerRegistry (backed by PurrNet PlayerID) for reliable alive tracking.
///   - Watches PlayerDeathHandler.isDead SyncVar on each target so auto-advance
///     fires the moment a spectated player dies, not one frame later.
///   - All spectator state is cleaned up properly on revive.
/// </summary>
[RequireComponent(typeof(PlayerDeathHandler))]
public class SpectatorController : NetworkBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Camera")]
    [Tooltip("The PlayerCameraRoot transform on this prefab.")]
    [SerializeField] private Transform _spectatorCameraRoot;

    [Tooltip("The PlayerFollowCamera (Cinemachine VCam) GameObject.")]
    [SerializeField] private GameObject _spectatorCameraObject;

    [Header("Input")]
    [Tooltip("Key to cycle to the next alive player.")]
    [SerializeField] private KeyCode _nextPlayerKey = KeyCode.RightArrow;

    [Tooltip("Key to cycle to the previous alive player.")]
    [SerializeField] private KeyCode _prevPlayerKey = KeyCode.LeftArrow;

    [Header("Offset from spectated player's camera root")]
    [SerializeField] private Vector3 _cameraOffset = new(0f, 0.5f, 0f);

    // ── Private state ──────────────────────────────────────────────────────

    private PlayerDeathHandler _deathHandler;
    private PlayerManager _playerManager;

    private bool _isSpectating;
    private int _spectatorIndex;
    private List<PlayerManager> _targets = new();

    // Subscribed target's death handler so we can react instantly when they die
    private PlayerDeathHandler _watchedTargetDeathHandler;

    // Original parent/position so we can restore on revive
    private Transform _originalCameraParent;
    private Vector3 _originalCameraLocalPos;
    private Quaternion _originalCameraLocalRot;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _deathHandler = GetComponent<PlayerDeathHandler>();
        _playerManager = GetComponent<PlayerManager>();
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!isOwner) return;

        if (_spectatorCameraRoot != null)
        {
            _originalCameraParent = _spectatorCameraRoot.parent;
            _originalCameraLocalPos = _spectatorCameraRoot.localPosition;
            _originalCameraLocalRot = _spectatorCameraRoot.localRotation;
        }

        _deathHandler.isDead.onChanged += OnDeadChanged;
    }

    protected override void OnDespawned(bool asServer)
    {
        if (!isOwner) return;
        _deathHandler.isDead.onChanged -= OnDeadChanged;
        UnwatchCurrentTarget();
    }

    private void Update()
    {
        if (!isOwner || !_isSpectating) return;
        HandleInput();
    }

    // ── Death / Revive callbacks ───────────────────────────────────────────

    private void OnDeadChanged(bool newVal)
    {
        if (!isOwner) return;

        if (newVal)
            BeginSpectating();
        else
            EndSpectating();
    }

    // ── Spectator logic ────────────────────────────────────────────────────

    private void BeginSpectating()
    {
        _isSpectating = true;
        _spectatorIndex = 0;

        RefreshTargetList();

        if (_targets.Count == 0)
        {
            PurrLogger.LogWarning("[SpectatorController] No alive players to spectate.");
            LocalPlayerUI.Instance?.OnSpectatorTargetChanged(null);
            return;
        }

        AttachToTarget(_spectatorIndex);
    }

    private void EndSpectating()
    {
        _isSpectating = false;
        UnwatchCurrentTarget();

        if (_spectatorCameraRoot != null)
        {
            _spectatorCameraRoot.SetParent(_originalCameraParent, false);
            _spectatorCameraRoot.localPosition = _originalCameraLocalPos;
            _spectatorCameraRoot.localRotation = _originalCameraLocalRot;
        }

        PurrLogger.Log("[SpectatorController] Spectator mode ended — player revived.");
    }

    private void HandleInput()
    {
        if (Input.GetKeyDown(_nextPlayerKey))
            CycleTarget(+1);
        else if (Input.GetKeyDown(_prevPlayerKey))
            CycleTarget(-1);
    }

    private void CycleTarget(int direction)
    {
        RefreshTargetList();
        if (_targets.Count == 0)
        {
            LocalPlayerUI.Instance?.OnSpectatorTargetChanged(null);
            return;
        }

        if (_spectatorCameraObject != null)
            _spectatorCameraObject.SetActive(false);

        _spectatorIndex = (_spectatorIndex + direction + _targets.Count) % _targets.Count;
        AttachToTarget(_spectatorIndex);
    }

    private void RefreshTargetList()
    {
        if (PlayerRegistry.Instance == null) return;
        _targets = PlayerRegistry.Instance.GetAlivePlayersExcept(_playerManager);
    }

    /// <summary>
    /// Re-parents our spectator camera root to the target's PlayerCameraRoot.
    /// Also subscribes to the target's isDead SyncVar so we auto-advance if they die.
    /// </summary>
    private void AttachToTarget(int index)
    {
        UnwatchCurrentTarget();

        if (index < 0 || index >= _targets.Count) return;

        PlayerManager target = _targets[index];
        if (target == null) return;

        // Watch this target's death state so we react the moment they die
        PlayerDeathHandler targetDeathHandler = target.GetComponent<PlayerDeathHandler>();
        if (targetDeathHandler != null)
        {
            targetDeathHandler.isDead.onChanged += OnSpectatedTargetDied;
            _watchedTargetDeathHandler = targetDeathHandler;
        }

        Transform targetCamRoot = FindCameraRoot(target.transform);

        if (targetCamRoot == null)
        {
            PurrLogger.LogWarning($"[SpectatorController] Could not find PlayerCameraRoot on {target.name}. Skipping.");
            // Try next available target rather than getting stuck
            if (_targets.Count > 1)
                CycleTarget(+1);
            return;
        }

        targetCamRoot.gameObject.SetActive(true);

        if (_spectatorCameraRoot != null)
        {
            _spectatorCameraRoot.SetParent(targetCamRoot, false);
            _spectatorCameraRoot.localPosition = _cameraOffset;
            _spectatorCameraRoot.localRotation = Quaternion.identity;
        }

        if (_spectatorCameraObject != null)
            _spectatorCameraObject.SetActive(true);

        LocalPlayerUI.Instance?.OnSpectatorTargetChanged(target);
        PurrLogger.Log($"[SpectatorController] Now spectating {target.name}");
    }

    /// <summary>
    /// Called via SyncVar callback the instant the spectated target dies.
    /// Automatically cycles to the next alive player.
    /// </summary>
    private void OnSpectatedTargetDied(bool isDead)
    {
        if (!isDead || !_isSpectating) return;

        PurrLogger.Log("[SpectatorController] Spectated player died — auto-advancing.");
        CycleTarget(+1);
    }

    /// <summary>Unsubscribes from the currently watched target's death event.</summary>
    private void UnwatchCurrentTarget()
    {
        if (_watchedTargetDeathHandler != null)
        {
            _watchedTargetDeathHandler.isDead.onChanged -= OnSpectatedTargetDied;
            _watchedTargetDeathHandler = null;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Searches direct children for a transform named "PlayerCameraRoot".</summary>
    private static Transform FindCameraRoot(Transform parent)
    {
        Transform direct = parent.Find("PlayerCameraRoot");
        if (direct != null) return direct;

        // Fallback: deep search
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "PlayerCameraRoot")
                return child;
        }

        return null;
    }
}