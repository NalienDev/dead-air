using PurrNet;
using UnityEngine;

/// <summary>
/// Snaps an object back to its last safe resting spot if it falls out of the level.
/// </summary>
[DisallowMultipleComponent]
public class OutOfBoundsGuard : MonoBehaviour
{
    [Tooltip("World Y below which the object is treated as out of bounds.")]
    [SerializeField] private float _killY = -25f;

    [Tooltip("Seconds between remembering the current spot as safe.")]
    [SerializeField] private float _safeSampleInterval = 0.5f;

    [Tooltip("A Rigidbody slower than this counts as resting.")]
    [SerializeField] private float _restingSpeed = 0.2f;

    private Rigidbody _rb;
    private CharacterController _cc;
    private NetworkIdentity _net;

    private Vector3 _safePos;
    private Quaternion _safeRot;
    private float _sampleTimer;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _cc = GetComponent<CharacterController>();
        _net = GetComponent<NetworkIdentity>();
        _safePos = transform.position;
        _safeRot = transform.rotation;
    }

    private void FixedUpdate()
    {
        // On a networked object only the authority moves it; everyone else mirrors it.
        if (_net != null && !_net.isOwner && !_net.isServer) return;

        if (transform.position.y < _killY)
        {
            Recover();
            return;
        }

        _sampleTimer -= Time.fixedDeltaTime;
        if (_sampleTimer > 0f) return;
        _sampleTimer = _safeSampleInterval;

        if (IsResting())
        {
            _safePos = transform.position;
            _safeRot = transform.rotation;
        }
    }

    // Only remember spots the object could actually sit at (a resting rigidbody or a
    // grounded character) so it never recovers back into mid-air or mid-fall.
    private bool IsResting()
    {
        if (_rb != null) return _rb.linearVelocity.magnitude <= _restingSpeed;
        if (_cc != null) return _cc.isGrounded;
        return true;
    }

    private void Recover()
    {
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = _safePos;
            _rb.rotation = _safeRot;
        }
        else if (_cc != null)
        {
            // A CharacterController resists being teleported while enabled.
            _cc.enabled = false;
            transform.SetPositionAndRotation(_safePos, _safeRot);
            _cc.enabled = true;
        }
        else
        {
            transform.SetPositionAndRotation(_safePos, _safeRot);
        }

        Debug.Log($"[OutOfBoundsGuard] '{name}' fell out of bounds — recovered to {_safePos}.");
    }
}
