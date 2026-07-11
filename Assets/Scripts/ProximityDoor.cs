using UnityEngine;

/// <summary>
/// Door that opens while a player or enemy is nearby, with hysteresis so it doesn't flap.
/// </summary>
public class ProximityDoor : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Animator with a bool parameter that opens the door when true.")]
    [SerializeField] private Animator _animator;
    [Tooltip("Point the proximity is measured from. Defaults to this object.")]
    [SerializeField] private Transform _sensor;

    [Header("Detection")]
    [Tooltip("Layers that open the door.")]
    [SerializeField] private LayerMask _detectorLayers = ~0;
    [Tooltip("A detector within this distance opens the door.")]
    [SerializeField] private float _openRadius = 3f;
    [Tooltip("The door closes once every detector is beyond this distance.")]
    [SerializeField] private float _closeRadius = 3.5f;
    [Tooltip("Seconds between proximity checks.")]
    [SerializeField] private float _checkInterval = 0.15f;

    [Header("Animator")]
    [Tooltip("Bool parameter on the Animator that holds the door open while true.")]
    [SerializeField] private string _openParameter = "IsOpen";

    [Header("Audio")]
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
