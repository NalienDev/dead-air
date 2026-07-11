using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local third-person spectator camera that orbit-follows other alive players after death.
/// </summary>
[DisallowMultipleComponent]
public class SpectatorController : MonoBehaviour
{
    [Header("Third-person framing")]
    [Tooltip("How far behind the spectated player the camera sits.")]
    [SerializeField] private float _distance = 4f;

    [Tooltip("How high above the spectated player the camera sits.")]
    [SerializeField] private float _height = 1.8f;

    [Tooltip("Point on the target the camera looks at, measured up from their feet.")]
    [SerializeField] private float _lookHeight = 1.4f;

    [Header("Collision")]
    [Tooltip("Layers the camera should not clip through. Exclude the Player layer.")]
    [SerializeField] private LayerMask _collisionMask = ~(1 << 6);
    [Tooltip("Keeps the camera this far off surfaces it collides with.")]
    [SerializeField] private float _collisionRadius = 0.3f;

    [Header("Input")]
    [SerializeField] private KeyCode _nextKey = KeyCode.RightArrow;
    [SerializeField] private KeyCode _prevKey = KeyCode.LeftArrow;

    private bool _isSpectating;
    private Transform _cameraTarget;          // local PlayerCameraRoot (vcam follows this)
    private PlayerManager _self;

    private readonly List<PlayerManager> _targets = new();
    private int _index;

    // Saved so the camera root snaps back to first-person on revive.
    private Vector3 _originalLocalPos;
    private Quaternion _originalLocalRot;
    private bool _savedTransform;

    private PlayerManager CurrentTarget =>
        (_index >= 0 && _index < _targets.Count) ? _targets[_index] : null;

    public PlayerManager CurrentlyWatching => CurrentTarget;

    public bool IsSpectating => _isSpectating;

    public void BeginSpectating(PlayerManager self, Transform cameraTarget)
    {
        _self = self;
        _cameraTarget = cameraTarget;

        if (_cameraTarget != null && !_savedTransform)
        {
            _originalLocalPos = _cameraTarget.localPosition;
            _originalLocalRot = _cameraTarget.localRotation;
            _savedTransform = true;
        }

        _isSpectating = true;
        _index = 0;
        RefreshTargets();
    }

    public void EndSpectating()
    {
        _isSpectating = false;
        _targets.Clear();

        // Restore the camera root so the FirstPersonController resumes cleanly.
        if (_cameraTarget != null && _savedTransform)
        {
            _cameraTarget.localPosition = _originalLocalPos;
            _cameraTarget.localRotation = _originalLocalRot;
        }
        _savedTransform = false;
    }

    private void Update()
    {
        if (!_isSpectating) return;

        if (Input.GetKeyDown(_nextKey)) Cycle(+1);
        else if (Input.GetKeyDown(_prevKey)) Cycle(-1);

        // Auto-advance if the player we were watching died or disconnected.
        if (CurrentTarget == null || CurrentTarget.IsDead)
            RefreshTargets();
    }

    private void LateUpdate()
    {
        if (!_isSpectating || _cameraTarget == null) return;

        PlayerManager target = CurrentTarget;
        if (target == null) return;

        Transform t = target.transform;
        Vector3 focus = t.position + Vector3.up * _lookHeight;
        Vector3 pos = focus - t.forward * _distance + Vector3.up * _height;

        // Pull the camera in to the first wall hit so it can't see outside the dungeon.
        Vector3 dir = pos - focus;
        float dist = dir.magnitude;
        if (dist > 0.001f &&
            Physics.SphereCast(focus, _collisionRadius, dir / dist, out RaycastHit hit,
                               dist, _collisionMask, QueryTriggerInteraction.Ignore))
        {
            pos = focus + dir / dist * Mathf.Max(0.1f, hit.distance - _collisionRadius);
        }

        _cameraTarget.position = pos;

        Vector3 lookDir = focus - pos;
        if (lookDir.sqrMagnitude > 0.0001f)
            _cameraTarget.rotation = Quaternion.LookRotation(lookDir);
    }

    private void Cycle(int dir)
    {
        RefreshTargets();
        if (_targets.Count == 0) return;
        _index = (_index + dir + _targets.Count) % _targets.Count;
    }

    // Rebuilds the alive-player list, keeping the current target when possible. Sorted so
    // cycling has a stable order, since FindObjectsByType doesn't guarantee one.
    private void RefreshTargets()
    {
        PlayerManager keep = CurrentTarget;

        _targets.Clear();
        foreach (PlayerManager p in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
        {
            if (p == null || p == _self || p.IsDead) continue;
            _targets.Add(p);
        }
        _targets.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        if (_targets.Count == 0) { _index = 0; return; }

        int found = keep != null ? _targets.IndexOf(keep) : -1;
        _index = found >= 0 ? found : Mathf.Clamp(_index, 0, _targets.Count - 1);
    }
}
