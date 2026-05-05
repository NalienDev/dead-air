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
/// </summary>
[RequireComponent(typeof(PlayerDeathHandler))]
public class SpectatorController : NetworkIdentity
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
    private bool _isSpectating;
    private int _spectatorIndex;
    private List<PlayerManager> _targets = new();

    // Original parent/position so we can restore on revive
    private Transform _originalCameraParent;
    private Vector3 _originalCameraLocalPos;
    private Quaternion _originalCameraLocalRot;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _deathHandler = GetComponent<PlayerDeathHandler>();
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!isOwner) return;

        // Cache original camera transform so we can restore it on revive
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
    }

    private void Update()
    {
        if (!isOwner || !_isSpectating) return;

        HandleInput();
        FollowCurrentTarget();
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
            return;
        }

        AttachToTarget(_spectatorIndex);
        PurrLogger.Log($"[SpectatorController] Spectating {_targets[_spectatorIndex].name}");
    }

    private void EndSpectating()
    {
        _isSpectating = false;

        // Restore camera root to original parent & transform
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

        if (_targets.Count == 0) return;
        if (_spectatorCameraRoot != null) _spectatorCameraObject.gameObject.SetActive(false);

        _spectatorIndex = (_spectatorIndex + direction + _targets.Count) % _targets.Count;
        AttachToTarget(_spectatorIndex);
        PurrLogger.Log($"[SpectatorController] Now spectating {_targets[_spectatorIndex].name}");
    }

    private void RefreshTargetList()
    {
        if (PlayerRegistry.Instance == null) return;
        _targets = PlayerRegistry.Instance.GetAlivePlayersExcept(GetComponent<PlayerManager>());
    }

    /// <summary>
    /// Re-parents our spectator camera root to the target's PlayerCameraRoot transform
    /// so the Cinemachine virtual camera naturally follows them.
    /// </summary>
    private void AttachToTarget(int index)
    {
        if (index < 0 || index >= _targets.Count) return;

        var target = _targets[index];

        // Find the target's camera root — it's named "PlayerCameraRoot" by convention
        Transform targetCamRoot = FindCameraRoot(target.transform);
        
        if (targetCamRoot == null)
        {
            PurrLogger.LogWarning($"[SpectatorController] Could not find PlayerCameraRoot on {target.name}");
            return;
        }
        targetCamRoot.gameObject.SetActive(true);
        if (_spectatorCameraRoot != null)
        {
            _spectatorCameraRoot.SetParent(targetCamRoot, false);
            _spectatorCameraRoot.localPosition = _cameraOffset;
            _spectatorCameraRoot.localRotation = Quaternion.identity;
        }

        // Notify UI about who we're watching
        LocalPlayerUI.Instance?.OnSpectatorTargetChanged(target);
    }

    private void FollowCurrentTarget()
    {
        // Safety: if target died while we were spectating, auto-advance
        if (_targets.Count == 0 || _spectatorIndex >= _targets.Count) return;

        var current = _targets[_spectatorIndex];
        if (current == null || current.IsDead)
            CycleTarget(+1);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Searches direct children for a transform named "PlayerCameraRoot".</summary>
    private static Transform FindCameraRoot(Transform parent)
    {
        // First try direct child (most common case)
        var direct = parent.Find("PlayerCameraRoot");
        if (direct != null) return direct;

        // Fallback: deep search
        foreach (Transform child in parent.GetComponentsInChildren<Transform>())
        {
            if (child.name == "PlayerCameraRoot")
                return child;
        }

        return null;
    }
}