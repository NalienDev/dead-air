using PurrNet;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkTransform))]
public class GrabbableObject : Interactable
{
    [SerializeField] private string _heldLayerName = "Held";

    private Rigidbody _rb;
    private Transform _pickupTarget;
    private int _originalLayer;
    private int _heldLayer;

    private SyncVar<bool> _isHeld = new SyncVar<bool>(false);

    private const float LerpSpeed = 10f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _originalLayer = gameObject.layer;
        _heldLayer = LayerMask.NameToLayer(_heldLayerName);
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerSetHeld(bool held)
    {
        _isHeld.value = held;
    }

    private bool TryGrab(GameObject user)
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


    private void Drop()
    {
        _pickupTarget = null;
        _rb.useGravity = true;
        _rb.isKinematic = false;
        gameObject.layer = _originalLayer;

        ServerSetHeld(false);
    }

    public void ForceDrop() => Drop();

    private void FixedUpdate()
    {
        if (_pickupTarget == null) return;

        _rb.MovePosition(Vector3.Lerp(transform.position,
                                      _pickupTarget.position,
                                      Time.deltaTime * LerpSpeed));
    }

    public override InteractionType OnInteract(GameObject user)
    {
        if (_isHeld.value) return InteractionType.NONE;

        bool grabbed = TryGrab(user);
        return grabbed ? InteractionType.GRAB : InteractionType.NONE;
    }
}