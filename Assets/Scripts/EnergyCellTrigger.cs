using System.Collections;
using PurrNet;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// The Dampener slot. Players push an <see cref="EnergyCell"/> into the slot's trigger;
/// the cell is consumed and the "silence zone" (the FogClearingZone transform) grows.
///
/// Fully server-authoritative and networked with PurrNet:
/// <list type="bullet">
/// <item>The client holding the cell asks the server to insert it (ServerRpc).</item>
/// <item>The server validates, destroys the cell (despawning it for everyone), and
/// bumps a replicated cell count.</item>
/// <item>Every client reacts to that count change: a snap object appears in the slot,
/// an insert sound plays, the zone smoothly expands over a few seconds, and a light
/// beam switches on shortly after.</item>
/// </list>
/// Because the state is driven by a <see cref="SyncVar{T}"/>, late-joining players see
/// the correct (already-expanded) zone immediately.
///
/// Keep this component's GameObject as a networked scene object (same as QuotaManager /
/// RoverManager). It needs a trigger Collider.
/// </summary>
public class EnergyCellTrigger : NetworkIdentity
{
    [Header("Slot")]
    [Tooltip("Object enabled in the slot when a cell is inserted, so it looks snapped in.")]
    [SerializeField] private GameObject _snapVisual;
    [Tooltip("Max cells this dampener accepts. 0 = unlimited.")]
    [SerializeField] private int _maxCells = 0;

    [Header("Silence Zone")]
    [Tooltip("The FogClearingZone transform to grow. Its scale IS the zone size.")]
    [FormerlySerializedAs("_shieldTransform")]
    [SerializeField] private Transform _silenceZoneTransform;
    [Tooltip("How much the zone's local scale grows per inserted cell.")]
    [SerializeField] private Vector3 _expandPerCell = new Vector3(10f, 10f, 10f);
    [Tooltip("Seconds the zone takes to smoothly reach its new size after an insert.")]
    [SerializeField] private float _expandDuration = 5f;

    [Header("Light Beam")]
    [Tooltip("Beam object (mesh + beam material). Toggled on while the zone expands.")]
    [SerializeField] private GameObject _beamObject;
    [Tooltip("Seconds after inserting a cell before the beam appears.")]
    [SerializeField] private float _beamDelay = 1f;
    [Tooltip("Keep the beam on after the expansion finishes.")]
    [SerializeField] private bool _keepBeamAfterExpand = false;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _insertSound;

    // Replicated so every client (and late joiners) agree on how far it has expanded.
    private readonly SyncVar<int> _cellCount = new(0);

    private Vector3 _baseZoneScale = Vector3.one;
    private Coroutine _expandRoutine;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_silenceZoneTransform != null)
            _baseZoneScale = _silenceZoneTransform.localScale;
    }

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        _cellCount.onChanged += OnCellCountChanged;

        // Snap to the correct state right away (handles late joiners — no animation).
        ApplyInstant(_cellCount.value);
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        _cellCount.onChanged -= OnCellCountChanged;
        if (_expandRoutine != null) StopCoroutine(_expandRoutine);
    }

    // ── Insertion ────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        // Only an energy cell counts.
        if (!other.TryGetComponent(out EnergyCell cell) &&
            (cell = other.GetComponentInParent<EnergyCell>()) == null)
            return;

        // Only the client that actually brought the cell drives the insert, so we
        // don't get one request per connected player.
        if (!cell.isOwner) return;
        if (_maxCells > 0 && _cellCount.value >= _maxCells) return;

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
        if (_maxCells > 0 && _cellCount.value >= _maxCells) return;

        _cellCount.value += 1;   // replicates → OnCellCountChanged on every client
        Destroy(cell.gameObject); // server-side destroy despawns it for everyone
    }

    // ── Presentation (runs on every client via the SyncVar) ──────────────────

    private void OnCellCountChanged(int newCount)
    {
        if (_snapVisual != null) _snapVisual.SetActive(newCount > 0);

        PlayInsertSound();

        if (_expandRoutine != null) StopCoroutine(_expandRoutine);
        _expandRoutine = StartCoroutine(ExpandRoutine(newCount));
    }

    private IEnumerator ExpandRoutine(int count)
    {
        if (_silenceZoneTransform == null) yield break;

        Vector3 from = _silenceZoneTransform.localScale;
        Vector3 to = _baseZoneScale + _expandPerCell * count;

        float t = 0f;
        bool beamOn = false;

        while (t < _expandDuration)
        {
            t += Time.deltaTime;

            if (!beamOn && t >= _beamDelay)
            {
                beamOn = true;
                if (_beamObject != null) _beamObject.SetActive(true);
            }

            _silenceZoneTransform.localScale =
                Vector3.Lerp(from, to, _expandDuration > 0f ? t / _expandDuration : 1f);
            yield return null;
        }

        _silenceZoneTransform.localScale = to;

        if (_beamObject != null && !_keepBeamAfterExpand)
            _beamObject.SetActive(false);

        _expandRoutine = null;
    }

    private void ApplyInstant(int count)
    {
        if (_silenceZoneTransform != null)
            _silenceZoneTransform.localScale = _baseZoneScale + _expandPerCell * count;

        if (_snapVisual != null) _snapVisual.SetActive(count > 0);
        if (_beamObject != null) _beamObject.SetActive(_keepBeamAfterExpand && count > 0);
    }

    private void PlayInsertSound()
    {
        if (_insertSound == null) return;
        if (_audioSource != null) _audioSource.PlayOneShot(_insertSound);
        else AudioSource.PlayClipAtPoint(_insertSound, transform.position);
    }
}
