using System.Collections;
using PurrNet;
using UnityEngine;

/// <summary>
/// Tracks energy cells fed into its slots, grows the silence zone as they arrive, and triggers victory once enough are in.
/// </summary>
public class Dampener : NetworkIdentity
{
    [Header("Silence Zone")]
    [Tooltip("Zone transform to grow; its scale is the zone size.")]
    [SerializeField] private Transform _silenceZoneTransform;
    [Tooltip("Local scale added per inserted cell.")]
    [SerializeField] private Vector3 _expandPerCell = new Vector3(10f, 10f, 10f);
    [Tooltip("Seconds to reach the new size after an insert.")]
    [SerializeField] private float _expandDuration = 5f;
    [Tooltip("Max cells accepted across all slots. 0 = unlimited.")]
    [SerializeField] private int _maxCells = 0;

    [Header("Light Beam")]
    [Tooltip("Beam object, pulsed on while the zone expands.")]
    [SerializeField] private GameObject _beamObject;
    [Tooltip("Seconds after a cell is registered before the beam appears.")]
    [SerializeField] private float _beamDelay = 1f;
    [Tooltip("Keep the beam on after the expansion finishes.")]
    [SerializeField] private bool _keepBeamAfterExpand = false;

    [Header("Audio")]
    [Tooltip("Source the beam sound plays from; falls back to PlayClipAtPoint if empty.")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Played on every client when the beam activates.")]
    [SerializeField] private AudioClip _beamActivateSound;

    [Header("Victory")]
    [Tooltip("Scene everyone is sent to once every cell is in.")]
    [SerializeField] private string _victorySceneName = "Victory";
    [Tooltip("Cells needed to win. 0 = all slots under this Dampener.")]
    [SerializeField] private int _cellsToWin = 0;
    [Tooltip("Seconds to wait after the final cell before loading Victory.")]
    [SerializeField] private float _victoryDelay = 3f;

    // Replicated so every client (and late joiners) agree on how far it has expanded.
    private readonly SyncVar<int> _cellCount = new(0);

    private Vector3 _baseZoneScale = Vector3.one;
    private Coroutine _expandRoutine;

    private int _winTarget;          // server-only: cells needed to win, resolved on spawn
    private bool _victoryTriggered;  // server-only: guard so victory fires once

    public int CellCount => _cellCount.value;

    public bool CanAcceptCell => _maxCells <= 0 || _cellCount.value < _maxCells;

    private void Awake()
    {
        if (_silenceZoneTransform != null)
            _baseZoneScale = _silenceZoneTransform.localScale;
    }

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        _cellCount.onChanged += OnCellCountChanged;

        // Snap to the correct state for late joiners, without animating.
        ApplyInstant(_cellCount.value);

        if (asServer)
            _winTarget = ResolveWinTarget();
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        _cellCount.onChanged -= OnCellCountChanged;
        if (_expandRoutine != null) StopCoroutine(_expandRoutine);
    }

    // Server only; a slot calls this on insert and gets back whether the cell was accepted.
    public bool ServerTryRegisterCell()
    {
        if (!isServer) return false;
        if (!CanAcceptCell) return false;

        _cellCount.value += 1;

        if (!_victoryTriggered && _winTarget > 0 && _cellCount.value >= _winTarget)
        {
            _victoryTriggered = true;
            StartCoroutine(ServerWinSequence());
        }

        return true;
    }

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
                PlayBeamSound();
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
