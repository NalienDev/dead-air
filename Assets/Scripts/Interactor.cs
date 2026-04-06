using PurrNet;
using UnityEngine;

public class Interactor : MonoBehaviour
{
    [SerializeField] private Transform _pickupPosTransform;
    [SerializeField] private float _interactRange = 2f;
    [SerializeField] private LayerMask _interactableLayers = Physics.DefaultRaycastLayers;

    private GrabbableObject _heldObject;
    private Camera _cam;

    private void Awake()
    {
        _cam = Camera.main;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;

        // If holding something, E always drops
        if (_heldObject != null)
        {
            _heldObject.ForceDrop();
            _heldObject = null;
            return;
        }

        Interactable interactable = null;
        bool hitSomething = Physics.Raycast(_cam.transform.position, _cam.transform.forward,
                                            out RaycastHit hit, _interactRange, _interactableLayers)
                            && hit.transform.TryGetComponent(out interactable);

        if (!hitSomething) return;

        InteractionType result = interactable.OnInteract(gameObject);

        switch (result)
        {
            case InteractionType.GRAB:
                _heldObject = hit.transform.GetComponent<GrabbableObject>();
                break;
        }
    }

    public Transform GetPickupPos() => _pickupPosTransform;
    public bool IsHolding => _heldObject != null;
}