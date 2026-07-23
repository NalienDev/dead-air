using PurrNet;
using UnityEngine;

/// <summary>
/// Networked physics object that players can pick up, hold, throw, and stash in inventory.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkTransform))]
public class GrabbableObject : Interactable
{
    [Header("HUD")]
    [SerializeField] private Sprite _icon;
    public Sprite Icon => _icon;
    
    [SerializeField] private string _heldLayerName = "Held";
    [SerializeField] private float _throwForce = 8f;

    private Rigidbody _rb;
    private Transform _pickupTarget;
    private int _originalLayer;
    private int _heldLayer;

    private Renderer[] _renderers;
    private Collider[] _colliders;

    private SyncVar<bool> _isHeld = new SyncVar<bool>(false);

    // Loot baked into room prefabs is frozen while the dungeon generates (rooms
    // teleport around) and swept up by the generator on teardown.
    private bool _isDungeonLoot;
    private bool _wasEverHeld;              // server-side: a player held this at least once
    private bool _frozenForGeneration;
    private DungeonGenerator _generator;    // cached so unsubscribe survives teardown

    /// <summary>True if this object was spawned as part of a dungeon room prefab.</summary>
    public bool IsDungeonLoot => _isDungeonLoot;

    // Marks loot the generator spawned at runtime (LootSpawnPoints) as dungeon loot, so the
    // generator's cleanup sweeps own it just like loot baked into room prefabs. Server-side
    // is enough: cleanup runs on the server and the despawn propagates.
    public void ServerMarkAsDungeonLoot() => _isDungeonLoot = true;

    /// <summary>Server-side: true once any player has picked this object up.</summary>
    public bool WasEverHeld => _wasEverHeld;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _originalLayer = gameObject.layer;
        _heldLayer = LayerMask.NameToLayer(_heldLayerName);

        _renderers = GetComponentsInChildren<Renderer>();
        _colliders = GetComponentsInChildren<Collider>();

        // Freeze as early as possible, since Awake runs during the room's Instantiate,
        // before the generator has aligned the room into place.
        _isDungeonLoot = GetComponentInParent<DungeonPart>() != null;
        if (_isDungeonLoot && DungeonGenerator.Instance != null
            && !DungeonGenerator.Instance.IsGenerated())
        {
            SetGenerationFreeze(true);
        }
    }

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        if (!_isDungeonLoot) return;

        if (_generator == null && DungeonGenerator.Instance != null)
        {
            _generator = DungeonGenerator.Instance;
            _generator.isGenerated.onChanged += OnDungeonGeneratedChanged;

            if (!_generator.IsGenerated())
                SetGenerationFreeze(true);
        }
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);

        if (_generator != null)
        {
            _generator.isGenerated.onChanged -= OnDungeonGeneratedChanged;
            _generator = null;
        }
    }

    private void OnDungeonGeneratedChanged(bool generated)
    {
        if (generated)
        {
            SetGenerationFreeze(false);
        }
        else if (!_isHeld.value && GetComponentInParent<DungeonPart>() != null)
        {
            // Regeneration started, so refreeze loot still sitting in a room. Items
            // players carried out (no room parent anymore) keep their physics.
            SetGenerationFreeze(true);
        }
    }

    private void SetGenerationFreeze(bool frozen)
    {
        if (_frozenForGeneration == frozen) return;
        _frozenForGeneration = frozen;

        if (frozen)
        {
            _rb.isKinematic = true;
        }
        else if (_pickupTarget == null && !_isHeld.value)
        {
            _rb.isKinematic = false;
            _rb.useGravity = true;
        }
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerSetHeld(bool held)
    {
        _isHeld.value = held;
        if (held) _wasEverHeld = true;
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

        if (!_frozenForGeneration)
        {
            _rb.useGravity = true;
            _rb.isKinematic = false;
        }
        gameObject.layer = _originalLayer;

        SetVisible(true);
        ServerSetHeld(false);
    }

    public void Throw(Vector3 direction)
    {
        Drop();
        _rb.AddForce(direction * _throwForce, ForceMode.Impulse);
    }

    // Shows/hides the object and its colliders locally, then relays through the
    // server so every other client matches.
    public void SetVisible(bool visible)
    {
        ApplyVisible(visible);
        ServerSetVisible(visible);
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerSetVisible(bool visible)
    {
        RpcSetVisible(visible);
    }

    [ObserversRpc(runLocally: false)]
    private void RpcSetVisible(bool visible)
    {
        ApplyVisible(visible);
    }

    private void ApplyVisible(bool visible)
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