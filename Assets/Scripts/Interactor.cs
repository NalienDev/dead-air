using PurrNet;
using UnityEngine;

public class Interactor : MonoBehaviour
{
    private const int InventorySize = 2;

    [SerializeField] private Transform _pickupPosTransform;
    [SerializeField] private float _interactRange = 2f;
    [SerializeField] private LayerMask _interactableLayers = Physics.DefaultRaycastLayers;

    private readonly GrabbableObject[] _slots = new GrabbableObject[InventorySize];
    private int _activeSlot = 0;
    private Camera _cam;

    public int ActiveSlot => _activeSlot;
    public GrabbableObject[] Slots => _slots;

    private void Awake() => _cam = Camera.main;

    private void Update()
    {
        HandleSlotSwitch();
        HandleInteract();
        HandleDrop();
        HandleThrow();
    }

    // ── Slot switching ─────────────────────────────────────────────────────
    private void HandleSlotSwitch()
    {
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (scroll == 0f) return;

        int newSlot = (_activeSlot + (scroll > 0f ? -1 : 1) + InventorySize) % InventorySize;
        if (newSlot == _activeSlot) return;

        _slots[_activeSlot]?.SetVisible(false);
        _activeSlot = newSlot;
        _slots[_activeSlot]?.SetVisible(true);
    }

    // ── Interact ───────────────────────────────────────────────────────────
    private void HandleInteract()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;
        if (!TryRaycast(out Interactable interactable, out RaycastHit hit)) return;

        // If the target is a grabbable and the active slot is already occupied,
        // bail out before calling OnInteract. This MUST be checked client-side
        // before the call — _isHeld is a SyncVar with replication latency, so
        // by the time it flips to true a second E press could already call
        // TryPickup and jam two objects into the same slot.
        if (hit.transform.TryGetComponent(out GrabbableObject _) && _slots[_activeSlot] != null)
            return;

        InteractionType result = interactable.OnInteract(gameObject);

        if (result == InteractionType.GRAB)
            _slots[_activeSlot] = hit.transform.GetComponent<GrabbableObject>();

        // InteractionType.PRESS needs no handling here — the interactable
        // already did its work inside OnInteract.
    }

    // ── Drop ──────────────────────────────────────────────────────────────
    private void HandleDrop()
    {
        if (!Input.GetKeyDown(KeyCode.Q)) return;
        if (_slots[_activeSlot] == null) return;

        _slots[_activeSlot].Drop();
        _slots[_activeSlot] = null;
    }

    // ── Throw ─────────────────────────────────────────────────────────────
    private void HandleThrow()
    {
        if (!Input.GetKeyDown(KeyCode.G)) return;
        if (_slots[_activeSlot] == null) return;

        _slots[_activeSlot].Throw(_cam.transform.forward);
        _slots[_activeSlot] = null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private bool TryRaycast(out Interactable interactable, out RaycastHit hit)
    {
        interactable = null;
        return Physics.Raycast(_cam.transform.position, _cam.transform.forward,
                               out hit, _interactRange, _interactableLayers)
               && hit.transform.TryGetComponent(out interactable);
    }

    public Transform GetPickupPos() => _pickupPosTransform;
    public bool IsHolding => _slots[_activeSlot] != null;
}