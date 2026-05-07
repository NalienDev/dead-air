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

    private void Awake()
    {
        _cam = Camera.main;
    }

    private void Update()
    {
        HandleSlotSwitch();
        HandlePickup();
        HandleDrop();
        HandleThrow();
    }

    // ── Slot switching ────────────────────────────────────────────────────────

    private void HandleSlotSwitch()
    {
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (scroll == 0f) return;

        int direction = scroll > 0f ? -1 : 1;
        int newSlot = (_activeSlot + direction + InventorySize) % InventorySize;

        if (newSlot == _activeSlot) return;

        // Hide the item leaving the active slot
        _slots[_activeSlot]?.SetVisible(false);

        _activeSlot = newSlot;

        // Show the item entering the active slot
        _slots[_activeSlot]?.SetVisible(true);
    }

    // ── Pick up ───────────────────────────────────────────────────────────────

    private void HandlePickup()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;
        if (_slots[_activeSlot] != null) return;  // slot occupied

        if (!TryRaycast(out Interactable interactable, out RaycastHit hit)) return;

        InteractionType result = interactable.OnInteract(gameObject);

        if (result == InteractionType.GRAB)
            _slots[_activeSlot] = hit.transform.GetComponent<GrabbableObject>();
    }

    // ── Drop ──────────────────────────────────────────────────────────────────

    private void HandleDrop()
    {
        if (!Input.GetKeyDown(KeyCode.Q)) return;
        if (_slots[_activeSlot] == null) return;

        _slots[_activeSlot].Drop();
        _slots[_activeSlot] = null;
    }

    // ── Throw ─────────────────────────────────────────────────────────────────

    private void HandleThrow()
    {
        if (!Input.GetKeyDown(KeyCode.G)) return;
        if (_slots[_activeSlot] == null) return;

        _slots[_activeSlot].Throw(_cam.transform.forward);
        _slots[_activeSlot] = null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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