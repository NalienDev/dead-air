// SuckableObject.cs
using UnityEngine;

/// <summary>
/// Attach to any prefab that should be pulled toward a SuctionZone.
/// Movement only activates when a SuctionZone registers this object.
/// The zone's collider shape defines the attraction area — no range value needed here.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SuckableObject : MonoBehaviour
{

    [Header("Attraction Settings")]
    [SerializeField] private float _attractionStartSpeed = 1f;
    [SerializeField] private float _attractionMaxSpeed = 10f;
    [SerializeField] private float _accelerationSpeed = 1f;
    [SerializeField] private AnimationCurve _velocityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);


    private Rigidbody _rb;
    private Transform _attractionTarget;
    private bool _isAttracting;
    private float _accelerationProgress;


    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (_rb.isKinematic) return;
        if (!_isAttracting || _attractionTarget == null) return;

        _accelerationProgress =
            Mathf.Clamp01(_accelerationProgress + Time.fixedDeltaTime * _accelerationSpeed);

        float currentSpeed = Mathf.Lerp(
            _attractionStartSpeed,
            _attractionMaxSpeed,
            _velocityCurve.Evaluate(_accelerationProgress)
        );

        Vector3 direction = (_attractionTarget.position - transform.position).normalized;
        _rb.linearVelocity = direction * currentSpeed;
    }

    public void BeginAttraction(Transform attractionTarget)
    {
        _attractionTarget = attractionTarget;
        _isAttracting = true;
        _accelerationProgress = 0f;
    }

    public void EndAttraction()
    {
        _isAttracting = false;
        _attractionTarget = null;
        _rb.linearVelocity = Vector3.zero;
    }
}