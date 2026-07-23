using System.Collections;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative expedition flow: departure, dungeon travel, and the return-to-base sequence.
/// </summary>
public class RoverManager : NetworkBehaviour
{
    public static RoverManager Instance { get; private set; }

    [Header("Lobby Drops")]
    [SerializeField] private Transform _lobbyDropPoint;
    [SerializeField] private GameObject _energyCellPrefab;

    [Header("Expedition Flow")]
    [Tooltip("Minimum seconds the loading screen stays up when departing.")]
    [SerializeField] private float _minLoadingScreenSeconds = 2f;
    [Tooltip("Seconds after an expedition starts before the return-to-base button works.")]
    [SerializeField] private float _returnLockSeconds = 60f;
    [Tooltip("Seconds the expedition-complete UI shows before players are teleported home.")]
    [SerializeField] private float _completeUISeconds = 4f;

    [Header("Sounds")]
    [Tooltip("Played when the expedition departs.")]
    [SerializeField] private AudioClip _expeditionStartSound;
    [Tooltip("Played when the expedition is completed.")]
    [SerializeField] private AudioClip _expeditionCompleteSound;

    // False while the early-return lock is armed. Replicated so every client's
    // ReturnToBaseButton agrees; the server also re-validates on use.
    private readonly SyncVar<bool> _canReturnToBase = new(true);

    private Coroutine _unlockRoutine;

    private Transform _expeditionSpawnPoint;
    private int _energyCells;

    // Departure: set while waiting for generation and the minimum loading time.
    private bool _awaitingDeparture;
    private float _departAfterTime;

    // Guards the return completion sequence so it can't run twice.
    private bool _returnSequenceRunning;

    public bool CanReturnToBase => _canReturnToBase.value;

    public bool IsStartingExpedition => _awaitingDeparture;

    // Server time of the last completed return; zones use it to re-arm.
    public float LastReturnTime { get; private set; } = -999f;

    protected override void OnSpawned(bool asServer) => Instance = this;

    protected override void OnDespawned(bool asServer)
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // Departure poll: teleport once the dungeon is ready and the minimum loading time has elapsed.
        if (!_awaitingDeparture || !isServer) return;
        if (Time.time < _departAfterTime) return;

        DungeonGenerator gen = DungeonGenerator.Instance;
        if (gen != null && !gen.IsGenerated()) return;

        _awaitingDeparture = false;
        TeleportPlayersToExpedition();
    }

    // Banks bandwidth and energy into the quota immediately; energy cells are also counted
    // locally so they can be respawned in the lobby on return.
    public void StoreCargo(NetworkIdentity identity)
    {
        if (identity.TryGetComponent(out BandwidthObject bw))
        {
            QuotaManager.Instance?.ServerAddBandwidth(bw.BandwidthValue);
        }
        else if (identity.TryGetComponent(out EnergyCell _))
        {
            _energyCells++;
            QuotaManager.Instance?.ServerAddEnergyCells(1);
        }
        else
        {
            return;
        }

        Destroy(identity.gameObject);
    }

    // Server only; kicks off the departure sequence. Update polls isGenerated instead of
    // subscribing to OnGenerated because PurrNet's weaver can't emit that delegate here.
    public void ServerStartExpedition()
    {
        if (!isServer || _awaitingDeparture || _returnSequenceRunning) return;

        // Set the day-1 quota to the number of players present (no-op after the first expedition).
        QuotaManager.Instance?.ServerScaleQuotaForPlayerCount();

        if (_expeditionSpawnPoint == null)
        {
            GameObject spawnGo = GameObject.FindGameObjectWithTag("ExpeditionSpawn");
            if (spawnGo == null)
            {
                Debug.LogWarning("[RoverManager] No object tagged 'ExpeditionSpawn' found.");
                return;
            }
            _expeditionSpawnPoint = spawnGo.transform;
        }

        RpcExpeditionDeparting();

        _awaitingDeparture = true;
        _departAfterTime = Time.time + _minLoadingScreenSeconds;

        DungeonGenerator gen = DungeonGenerator.Instance;
        if (gen != null && !gen.IsGenerated())
        {
            Debug.Log("[RoverManager] Dungeon not generated; loading screen stays until it finishes.");
            gen.StartGeneration();
        }
    }

    // Departure presentation on every client: sound and loading screen.
    [ObserversRpc(runLocally: true)]
    private void RpcExpeditionDeparting()
    {
        PlayUISound(_expeditionStartSound);
        if (LoadingScreenManager.Instance != null)
            LoadingScreenManager.Instance.ShowLoadingScreen();
    }

    // Server only; sends everyone in and closes the loading screen on all clients.
    private void TeleportPlayersToExpedition()
    {
        ArmReturnLock();

        foreach (PlayerManager player in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
        {
            player.TeleportToPosition(_expeditionSpawnPoint.position, _expeditionSpawnPoint.rotation);
            player.SetInsideDungeon(true); // the Echo only hunts flagged players
        }

        SceneChanger.Instance?.RpcHideLoadingScreen();
    }

    // Server only; locks return-to-base for _returnLockSeconds.
    private void ArmReturnLock()
    {
        _canReturnToBase.value = false;

        if (_unlockRoutine != null) StopCoroutine(_unlockRoutine);
        _unlockRoutine = StartCoroutine(UnlockReturnAfterDelay());
    }

    private IEnumerator UnlockReturnAfterDelay()
    {
        yield return new WaitForSeconds(_returnLockSeconds);
        _canReturnToBase.value = true;
        _unlockRoutine = null;
        Debug.Log("[RoverManager] Return-to-base unlocked.");
    }

    [ServerRpc(requireOwnership: false)]
    public void ServerRequestReturnToBase(Vector3 teleportPos, Quaternion teleportRot)
        => RequestReturnServer(teleportPos, teleportRot);

    private void RequestReturnServer(Vector3 teleportPos, Quaternion teleportRot)
    {
        // Server-side re-validation; the ReturnToBaseButton checks are only local feedback.
        if (!_canReturnToBase.value)
        {
            Debug.Log("[RoverManager] Return denied, expedition just started.");
            return;
        }

        if (_returnSequenceRunning) return;

        if (ReturnGatherZone.Instance != null && !ReturnGatherZone.Instance.AreAllAlivePlayersInside())
        {
            Debug.Log("[RoverManager] Return denied, not every player is in the return zone.");
            return;
        }

        StartCoroutine(ReturnSequence(teleportPos, teleportRot));
    }

    private IEnumerator ReturnSequence(Vector3 teleportPos, Quaternion teleportRot)
    {
        _returnSequenceRunning = true;

        // Completion fanfare first, held for a few seconds before anyone moves.
        RpcExpeditionComplete(_completeUISeconds);
        yield return new WaitForSeconds(_completeUISeconds);

        if (QuotaManager.Instance != null)
        {
            // Cargo was already banked in StoreCargo; just count the expedition and evaluate.
            QuotaManager.Instance.ServerRegisterExpeditionReturn();
            QuotaManager.Instance.ServerCheckQuotaAndProceed();
        }

        foreach (PlayerManager player in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
        {
            // Reset before the teleport so revived players move to the lobby with everyone else.
            player.ServerResetForNewExpedition();

            player.TeleportToPosition(teleportPos, teleportRot);
            player.SetInsideDungeon(false); // back in the lobby, hide from the Echo
        }

        // Stagger drops so they don't spawn on top of each other and explode.
        for (int i = 0; i < _energyCells; i++)
            Instantiate(_energyCellPrefab, _lobbyDropPoint.position + Vector3.up * (0.4f * i), _lobbyDropPoint.rotation);

        _energyCells = 0;
        LastReturnTime = Time.time; // the start zone re-arms off this
        _returnSequenceRunning = false;
    }

    // Completion presentation on every client: sound and splash UI.
    [ObserversRpc(runLocally: true)]
    private void RpcExpeditionComplete(float uiSeconds)
    {
        PlayUISound(_expeditionCompleteSound);
        ExpeditionCompleteUI.Instance?.Show(uiSeconds);
    }

    // Plays a flat 2D one-shot.
    private static void PlayUISound(AudioClip clip)
    {
        if (clip == null) return;

        var go = new GameObject("UISound");
        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 0f;
        src.PlayOneShot(clip);
        Destroy(go, clip.length + 0.5f);
    }
}
