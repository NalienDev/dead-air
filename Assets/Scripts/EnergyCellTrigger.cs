using PurrNet;
using UnityEngine;

/// <summary>
/// A Dampener slot that consumes an inserted energy cell and reports it to the Dampener.
/// </summary>
public class EnergyCellTrigger : NetworkIdentity
{
    [Header("Dampener")]
    [Tooltip("The Dampener this slot feeds. Auto-found on a parent if left empty.")]
    [SerializeField] private Dampener _dampener;

    [Header("Slot")]
    [Tooltip("Shown once a cell is inserted, so the slot looks snapped in.")]
    [SerializeField] private GameObject _snapVisual;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _insertSound;

    // Replicated so every client (and late joiners) agree this slot is occupied.
    private readonly SyncVar<bool> _filled = new(false);

    private void Awake()
    {
        if (_dampener == null) _dampener = GetComponentInParent<Dampener>();
    }

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        _filled.onChanged += OnFilledChanged;

        // Snap to the correct state for late joiners, without playing the sound.
        if (_snapVisual != null) _snapVisual.SetActive(_filled.value);
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        _filled.onChanged -= OnFilledChanged;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_filled.value) return;

        if (!other.TryGetComponent(out EnergyCell cell) &&
            (cell = other.GetComponentInParent<EnergyCell>()) == null)
            return;

        // Only the client carrying the cell drives the insert, to avoid one request per player.
        if (!cell.isOwner) return;
        if (_dampener == null || !_dampener.CanAcceptCell) return;

        // Drop it from inventory before it's destroyed so nothing keeps a dead reference.
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

        if (!_dampener.ServerTryRegisterCell()) return;

        _filled.value = true;
        Destroy(cell.gameObject);   // server-side destroy despawns it for everyone
    }

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
