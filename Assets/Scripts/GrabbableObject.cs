using PurrNet;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkTransform))]
public class GrabbableObject : Interactable
{
    [SerializeField] private string _heldLayerName = "Held";
    [SerializeField] private float _throwForce = 8f;

    private Rigidbody _rb;
    private Transform _pickupTarget;
    private int _originalLayer;
    private int _heldLayer;

    private Renderer[] _renderers;
    private Collider[] _colliders;

    private SyncVar<bool> _isHeld = new SyncVar<bool>(false);

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _originalLayer = gameObject.layer;
        _heldLayer = LayerMask.NameToLayer(_heldLayerName);

        _renderers = GetComponentsInChildren<Renderer>();
        _colliders = GetComponentsInChildren<Collider>();
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerSetHeld(bool held)
    {
        _isHeld.value = held;
    }

    public bool TryPickup(GameObject user)
    {
        if (_isHeld.value) return false;

        var objNet = GetComponent<NetworkTransform>();
        var userNet = user.GetComponent<NetworkTransform>();

        if (!objNet.isOwner && userNet.localPlayer.HasValue)
            objNet.GiveOwnership(userNet.localPlayer.Value);

        _pickupTarget = user.GetComponent<Interactor>().GetPickupPos();

        _rb.useGravity = false;
        _rb.isKinematic = true;

        if (_heldLayer != -1) gameObject.layer = _heldLayer;

        ServerSetHeld(true);
        return true;
    }

    public void Drop()
    {
        _pickupTarget = null;

        _rb.useGravity = true;
        _rb.isKinematic = false;
        gameObject.layer = _originalLayer;

        SetVisible(true);
        ServerSetHeld(false);
    }

    public void Throw(Vector3 direction)
    {
        Drop();
        _rb.AddForce(direction * _throwForce, ForceMode.Impulse);
    }

    /// <summary>
    /// Shows or hides this object while it sits in an inactive inventory slot.
    /// Colliders are also disabled so the hidden item doesn't block raycasts or physics.
    /// </summary>
    public void SetVisible(bool visible)
    {
        foreach (Renderer r in _renderers) r.enabled = visible;
        foreach (Collider c in _colliders) c.enabled = visible;

        // Keep the rigidbody sleeping when hidden so it doesn't drift
        if (!visible)
            _rb.Sleep();
    }

    private void FixedUpdate()
    {
        if (_pickupTarget == null) return;
        _rb.MovePosition(_pickupTarget.position);
    }

    public override InteractionType OnInteract(GameObject user)
    {
        if (_isHeld.value) return InteractionType.NONE;

        bool picked = TryPickup(user);
        return picked ? InteractionType.GRAB : InteractionType.NONE;
    }
}