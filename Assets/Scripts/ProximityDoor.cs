using UnityEngine;

/// <summary>
/// A generic proximity door. Put this on a door object whose Animator has an "open
/// and hold" clip and a "close" clip, driven by a single bool parameter (default
/// "IsOpen"): the animator opens on true and closes on false.
///
/// While anything on <see cref="_detectorLayers"/> (players, enemies…) is within
/// <see cref="_openRadius"/>, the door opens and stays open. It only closes once
/// everything has moved beyond <see cref="_closeRadius"/> — the gap between the two
/// gives it hysteresis so a body loitering on the threshold doesn't flap it open and
/// shut.
///
/// Detection is proximity-only (no colliders/triggers needed) and reads the synced
/// world positions of players and enemies, so every client drives its own door to the
/// same state without any networking of its own.
/// </summary>
public class ProximityDoor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Animator with a bool parameter that opens the door when true. Defaults to " +
             "an Animator on this object or its children.")]
    [SerializeField] private Animator _animator;
    [Tooltip("Optional. Point the proximity is measured from. Defaults to this object's " +
             "position — set it if the door's pivot isn't where players approach.")]
    [SerializeField] private Transform _sensor;

    [Header("Detection")]
    [Tooltip("Layers that open the door — set this to your Player and Enemy layers.")]
    [SerializeField] private LayerMask _detectorLayers = ~0;
    [Tooltip("A detector within this distance opens the door.")]
    [SerializeField] private float _openRadius = 3f;
    [Tooltip("The door closes once every detector is beyond this distance. Keep it a little " +
             "larger than the open radius so a body on the threshold doesn't flap the door.")]
    [SerializeField] private float _closeRadius = 3.5f;
    [Tooltip("Seconds between proximity checks. Small is fine — this is cheap.")]
    [SerializeField] private float _checkInterval = 0.15f;

    [Header("Animator")]
    [Tooltip("Bool parameter on the Animator that holds the door open while true.")]
    [SerializeField] private string _openParameter = "IsOpen";

    [Header("Audio (optional)")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _openSound;
    [SerializeField] private AudioClip _closeSound;

    private readonly Collider[] _hits = new Collider[16];
    private int _openParamHash;
    private bool _isOpen;
    private float _nextCheckTime;

    private void Awake()
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        if (_sensor == null) _sensor = transform;
        _openParamHash = Animator.StringToHash(_openParameter);

        // Match the animator to our starting (closed) state without a sound.
        if (_animator != null) _animator.SetBool(_openParamHash, false);
    }

    private void Update()
    {
        if (Time.time < _nextCheckTime) return;
        _nextCheckTime = Time.time + _checkInterval;

        // Hysteresis: while open, hold until everything clears the (larger) close
        // radius; while closed, only open once something is inside the open radius.
        float radius = _isOpen ? _closeRadius : _openRadius;
        bool detectorNear = AnyDetectorWithin(radius);

        if (detectorNear && !_isOpen) SetOpen(true);
        else if (!detectorNear && _isOpen) SetOpen(false);
    }

    private bool AnyDetectorWithin(float radius)
    {
        int count = Physics.OverlapSphereNonAlloc(
            _sensor.position, radius, _hits, _detectorLayers, QueryTriggerInteraction.Ignore);
        return count > 0;
    }

    private void SetOpen(bool open)
    {
        _isOpen = open;

        if (_animator != null) _animator.SetBool(_openParamHash, open);

        AudioClip clip = open ? _openSound : _closeSound;
        if (_audioSource != null && clip != null) _audioSource.PlayOneShot(clip);
    }

    private void OnDrawGizmosSelected()
    {
        Transform s = _sensor != null ? _sensor : transform;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(s.position, _openRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(s.position, _closeRadius);
    }
}
