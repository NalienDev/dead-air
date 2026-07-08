using PurrNet;
using UnityEngine;

/// <summary>
/// A Dampener slot. Players push an <see cref="EnergyCell"/> into the slot's trigger;
/// the cell is consumed, this slot shows its snapped-in visual and plays the insert
/// sound, and it tells its <see cref="Dampener"/> to grow the silence zone and pulse the
/// beam. One cell per slot.
///
/// Server-authoritative and networked with PurrNet:
/// <list type="bullet">
/// <item>The client holding the cell asks the server to insert it (ServerRpc).</item>
/// <item>The server validates, registers the cell with the <see cref="Dampener"/>,
/// destroys the cell (despawning it for everyone), and flips this slot's replicated
/// "filled" flag.</item>
/// <item>Every client reacts to that flag: the snap visual appears and the insert sound
/// plays. Growing the zone and pulsing the beam are the Dampener's job, driven by its
/// own replicated count.</item>
/// </list>
/// Put this on each slot object (a networked scene object with a trigger Collider) and
/// point it at the shared Dampener — it's auto-found on a parent if left empty.
/// </summary>
public class EnergyCellTrigger : NetworkIdentity
{
    [Header("Dampener")]
    [Tooltip("The Dampener this slot feeds. Auto-found on a parent if left empty.")]
    [SerializeField] private Dampener _dampener;

    [Header("Slot")]
    [Tooltip("Object enabled in the slot when a cell is inserted, so it looks snapped in.")]
    [SerializeField] private GameObject _snapVisual;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _insertSound;

    // Replicated so every client (and late joiners) agree this slot is occupied.
    private readonly SyncVar<bool> _filled = new(false);

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_dampener == null) _dampener = GetComponentInParent<Dampener>();
    }

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        _filled.onChanged += OnFilledChanged;

        // Snap to the correct state right away (handles late joiners — no sound).
        if (_snapVisual != null) _snapVisual.SetActive(_filled.value);
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        _filled.onChanged -= OnFilledChanged;
    }

    // ── Insertion ────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (_filled.value) return;

        // Only an energy cell counts.
        if (!other.TryGetComponent(out EnergyCell cell) &&
            (cell = other.GetComponentInParent<EnergyCell>()) == null)
            return;

        // Only the client that actually brought the cell drives the insert, so we
        // don't get one request per connected player.
        if (!cell.isOwner) return;
        if (_dampener == null || !_dampener.CanAcceptCell) return;

        // Clear it from the local inventory before it's destroyed so no slot keeps a
        // reference to a dead object.
        foreach (Interactor interactor in FindObjectsByType<Interactor>(FindObjectsSortMode.None))
            interactor.RemoveFromInventory(cell);

        RequestInsertCell(cell);
    }

    [ServerRpc(requireOwnership: false)]
    private void RequestInsertCell(NetworkIdentity cell)
    {
        if (cell == null) return;
        if (_filled.value) return;
        if (_dampener == null) return;

        // Ask the Dampener to take it — it owns the cell budget and the zone/beam.
        if (!_dampener.ServerTryRegisterCell()) return;

        _filled.value = true;       // replicates → OnFilledChanged on every client
        Destroy(cell.gameObject);   // server-side destroy despawns it for everyone
    }

    // ── Presentation (runs on every client via the SyncVar) ──────────────────

    private void OnFilledChanged(bool filled)
    {
        if (_snapVisual != null) _snapVisual.SetActive(filled);
        if (filled) PlayInsertSound();
    }

    private void PlayInsertSound()
    {
        if (_insertSound == null) return;
        if (_audioSource != null) _audioSource.PlayOneShot(_insertSound);
        else AudioSource.PlayClipAtPoint(_insertSound, transform.position);
    }
}
