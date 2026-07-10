using PurrNet;
using UnityEngine;

public class Interactor : MonoBehaviour
{
    [SerializeField] private int _baseInventorySize = 2;

    [SerializeField] private Transform _pickupPosTransform;
    [SerializeField] private float _interactRange = 2f;
    [SerializeField] private LayerMask _interactableLayers = Physics.DefaultRaycastLayers;

    private GrabbableObject[] _slots;
    private int _extraSlots = 0;
    private int _activeSlot = 0;
    private Camera _cam;

    public int ActiveSlot => _activeSlot;
    public GrabbableObject[] Slots => _slots;

    private void Awake()
    {
        _cam = Camera.main;
        // Guard against spawn-order races: if an upgrade already sized the inventory
        // (SetExtraSlots ran first), don't clobber it.
        if (_slots == null)
            _slots = new GrabbableObject[Mathf.Max(1, _baseInventorySize)];
    }

    /// <summary>
    /// Grows (or shrinks) the inventory to base + <paramref name="extra"/> slots,
    /// preserving items already held. Driven by the +inventory-slot upgrade.
    /// </summary>
    public void SetExtraSlots(int extra)
    {
        extra = Mathf.Max(0, extra);
        if (extra == _extraSlots && _slots != null) return;
        _extraSlots = extra;

        int newSize = Mathf.Max(1, _baseInventorySize + _extraSlots);
        var resized = new GrabbableObject[newSize];

        if (_slots != null)
            for (int i = 0; i < _slots.Length && i < newSize; i++)
                resized[i] = _slots[i];

        _slots = resized;
        if (_activeSlot >= _slots.Length) _activeSlot = 0;
    }

    private void Update()
    {
        HandleHover();
        HandleSlotSwitch();
        HandleInteract();
        HandleDrop();
        HandleThrow();
    }

    // ── Hover outline ──────────────────────────────────────────────────────
    // White silhouette on whatever interactable the camera is aimed at. Purely
    // local — this component only runs for the owning player.

    private Interactable _hovered;
    private HoverOutline _hoveredOutline;

    private void HandleHover()
    {
        Interactable current = null;
        if (TryRaycast(out Interactable interactable, out _))
            current = interactable;

        if (current == _hovered) return;

        if (_hoveredOutline != null) _hoveredOutline.SetHighlight(false);

        _hovered = current;
        _hoveredOutline = null;

        if (current != null)
        {
            if (!current.TryGetComponent(out _hoveredOutline))
                _hoveredOutline = current.gameObject.AddComponent<HoverOutline>();
            _hoveredOutline.SetHighlight(true);
        }
    }

    // ── Slot switching ─────────────────────────────────────────────────────
    private void HandleSlotSwitch()
    {
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (scroll == 0f) return;

        int size = _slots.Length;
        int newSlot = (_activeSlot + (scroll > 0f ? -1 : 1) + size) % size;
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

    /// <summary>
    /// Clears a specific object from the inventory without dropping/throwing it.
    /// Used when something consumes a held item (e.g. the dampener eats an energy
    /// cell) so we never keep a reference to a destroyed object in a slot.
    /// </summary>
    public void RemoveFromInventory(GrabbableObject obj)
    {
        for (int i = 0; i < _slots.Length; i++)
            if (_slots[i] == obj) _slots[i] = null;
    }
}