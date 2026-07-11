using System.Collections;
using PurrNet;
using UnityEngine;

/// <summary>
/// The Dampener core. Owns the "silence zone" (a FogClearingZone transform whose scale
/// IS the zone size) and the light beam, and tracks how many energy cells have been fed
/// in across all of its <see cref="EnergyCellTrigger"/> slots.
///
/// The slots are separate networked objects. When a player pushes a cell into a slot,
/// the slot (server-side) calls <see cref="ServerTryRegisterCell"/>; that bumps a
/// replicated count, and every client reacts by smoothly growing the zone and pulsing
/// the beam. Because the count is a <see cref="SyncVar{T}"/>, late joiners snap straight
/// to the already-expanded size.
///
/// Put this on the Dampener root (a networked scene object) and wire the zone transform
/// and beam here. The per-slot snap visual and the insert sound live on the slots.
///
/// When every cell is in (all its slots filled, or the explicit <c>_cellsToWin</c>),
/// the server sends all players to the Victory scene via <see cref="SceneChanger"/>.
/// </summary>
public class Dampener : NetworkIdentity
{
    [Header("Silence Zone")]
    [Tooltip("The FogClearingZone transform to grow. Its scale IS the zone size.")]
    [SerializeField] private Transform _silenceZoneTransform;
    [Tooltip("How much the zone's local scale grows per inserted cell.")]
    [SerializeField] private Vector3 _expandPerCell = new Vector3(10f, 10f, 10f);
    [Tooltip("Seconds the zone takes to smoothly reach its new size after an insert.")]
    [SerializeField] private float _expandDuration = 5f;
    [Tooltip("Max cells this dampener accepts across all its slots. 0 = unlimited.")]
    [SerializeField] private int _maxCells = 0;

    [Header("Light Beam")]
    [Tooltip("Beam object (mesh + beam material). Pulsed on while the zone expands.")]
    [SerializeField] private GameObject _beamObject;
    [Tooltip("Seconds after a cell is registered before the beam appears.")]
    [SerializeField] private float _beamDelay = 1f;
    [Tooltip("Keep the beam on after the expansion finishes.")]
    [SerializeField] private bool _keepBeamAfterExpand = false;

    [Header("Audio")]
    [Tooltip("Source the beam sound plays from (3D, at the dampener). Falls back to " +
             "PlayClipAtPoint if left empty.")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Played on every client the moment the light beam activates.")]
    [SerializeField] private AudioClip _beamActivateSound;

    [Header("Victory")]
    [Tooltip("Scene everyone is sent to once every cell is in. Loaded through " +
             "SceneChanger so the change is networked.")]
    [SerializeField] private string _victorySceneName = "Victory";
    [Tooltip("Cells needed to win. 0 = auto: the number of cell slots under this " +
             "Dampener (capped by Max Cells if that's set).")]
    [SerializeField] private int _cellsToWin = 0;
    [Tooltip("Seconds to wait after the final cell before loading Victory, so the " +
             "last zone expansion and beam can play.")]
    [SerializeField] private float _victoryDelay = 3f;

    // Replicated so every client (and late joiners) agree on how far it has expanded.
    private readonly SyncVar<int> _cellCount = new(0);

    private Vector3 _baseZoneScale = Vector3.one;
    private Coroutine _expandRoutine;

    private int _winTarget;          // server-only: cells needed to win, resolved on spawn
    private bool _victoryTriggered;  // server-only: guard so victory fires once

    /// <summary>Cells accepted so far.</summary>
    public int CellCount => _cellCount.value;

    /// <summary>True while the dampener still has room for another cell.</summary>
    public bool CanAcceptCell => _maxCells <= 0 || _cellCount.value < _maxCells;

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

        // Slots are scene objects, so they exist by now — count them once.
        if (asServer)
            _winTarget = ResolveWinTarget();
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        _cellCount.onChanged -= OnCellCountChanged;
        if (_expandRoutine != null) StopCoroutine(_expandRoutine);
    }

    // ── Registration (server-side, called by the slots) ──────────────────────

    /// <summary>
    /// Server only. A slot calls this when a cell is inserted. Bumps the replicated
    /// count — which grows the zone and pulses the beam on every client — and returns
    /// whether the cell was accepted (false if the dampener is already full).
    /// </summary>
    public bool ServerTryRegisterCell()
    {
        if (!isServer) return false;
        if (!CanAcceptCell) return false;

        _cellCount.value += 1;   // replicates → OnCellCountChanged on every client

        // Every cell is in — send everyone to the Victory scene (once).
        if (!_victoryTriggered && _winTarget > 0 && _cellCount.value >= _winTarget)
        {
            _victoryTriggered = true;
            StartCoroutine(ServerWinSequence());
        }

        return true;
    }

    // 0 in the inspector means "all my slots"; capped by Max Cells so a low cap can
    // still win even if there are physically more slots than it will accept.
    private int ResolveWinTarget()
    {
        int target = _cellsToWin > 0
            ? _cellsToWin
            : GetComponentsInChildren<EnergyCellTrigger>(true).Length;

        if (_maxCells > 0) target = Mathf.Min(target, _maxCells);
        return target;
    }

    private IEnumerator ServerWinSequence()
    {
        if (_victoryDelay > 0f)
            yield return new WaitForSeconds(_victoryDelay);

        if (SceneChanger.Instance != null)
            SceneChanger.Instance.LoadSceneForEveryone(_victorySceneName);
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(_victorySceneName);
    }

    // ── Presentation (runs on every client via the SyncVar) ──────────────────

    private void OnCellCountChanged(int newCount)
    {
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
                SetBeam(true);
                PlayBeamSound(); // activation moment only — ApplyInstant stays silent
            }

            _silenceZoneTransform.localScale =
                Vector3.Lerp(from, to, _expandDuration > 0f ? t / _expandDuration : 1f);
            yield return null;
        }

        _silenceZoneTransform.localScale = to;

        if (!_keepBeamAfterExpand) SetBeam(false);

        _expandRoutine = null;
    }

    private void ApplyInstant(int count)
    {
        if (_silenceZoneTransform != null)
            _silenceZoneTransform.localScale = _baseZoneScale + _expandPerCell * count;

        SetBeam(_keepBeamAfterExpand && count > 0);
    }

    private void SetBeam(bool on)
    {
        if (_beamObject != null) _beamObject.SetActive(on);
    }

    private void PlayBeamSound()
    {
        if (_beamActivateSound == null) return;
        if (_audioSource != null) _audioSource.PlayOneShot(_beamActivateSound);
        else AudioSource.PlayClipAtPoint(_beamActivateSound, transform.position);
    }
}
