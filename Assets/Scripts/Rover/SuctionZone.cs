// SuctionZone.cs
using UnityEngine;

/// <summary>
/// Attach to a GameObject that has a Trigger Collider.
/// Any SuckableObject that enters the trigger starts being attracted to
/// <see cref="_attractionTarget"/> (defaults to this transform if left null).
/// </summary>
[RequireComponent(typeof(Collider))]
public class SuctionZone : MonoBehaviour
{
    [Tooltip("The point suckable objects are pulled toward. Defaults to this transform.")]
    [SerializeField] private Transform _attractionTarget;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            Debug.LogWarning("[SuctionZone] Collider must be a trigger. Forcing isTrigger = true.", this);
            col.isTrigger = true;
        }

        _attractionTarget ??= transform;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out SuckableObject suckable))
            suckable.BeginAttraction(_attractionTarget);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent(out SuckableObject suckable))
            suckable.EndAttraction();
    }
}