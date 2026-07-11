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

    // ── Cargo (server) ────────────────────────────────────────────────────────

    // Server: called when the expedition sucker pulls something in. Bandwidth and energy
    // are banked into the quota IMMEDIATELY so the HUD updates the moment an item is
    // vacuumed up. Energy cells are also counted locally so they can be respawned in the
    // lobby on return.
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

    // ── Expedition start (server-driven by ExpeditionStartZone) ───────────────
    //
    // The WHOLE start flow runs on the server: StartGeneration() is a no-op on clients
    // and completion is only observable server-side. We POLL isGenerated in Update
    // instead of subscribing to DungeonGenerator.OnGenerated — PurrNet's weaver cannot
    // emit the method-group delegate (ldftn) such a subscription compiles to inside
    // RPC-reachable code (it threw InvalidProgramException).

    /// <summary>Server only. Kicks off the departure sequence.</summary>
    public void ServerStartExpedition()
    {
        if (!isServer || _awaitingDeparture || _returnSequenceRunning) return;

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
            Debug.Log("[RoverManager] Dungeon not generated — loading screen stays until it finishes.");
            gen.StartGeneration();
        }
    }

    // Departure presentation on every client: sound + loading screen.
    [ObserversRpc(runLocally: true)]
    private void RpcExpeditionDeparting()
    {
        PlayUISound(_expeditionStartSound);
        if (LoadingScreenManager.Instance != null)
            LoadingScreenManager.Instance.ShowLoadingScreen();
    }

    // Server only. Sends everyone in and closes the loading screen on all clients.
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

    // Server only. Locks return-to-base for _returnLockSeconds.
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

    // ── Return to base ────────────────────────────────────────────────────────

    [ServerRpc(requireOwnership: false)]
    public void ServerRequestReturnToBase(Vector3 teleportPos, Quaternion teleportRot)
        => RequestReturnServer(teleportPos, teleportRot);

    private void RequestReturnServer(Vector3 teleportPos, Quaternion teleportRot)
    {
        // Server-side re-validation — the client-side checks in ReturnToBaseButton are
        // only for instant feedback.
        if (!_canReturnToBase.value)
        {
            Debug.Log("[RoverManager] Return denied — expedition just started.");
            return;
        }

        if (_returnSequenceRunning) return;

        if (ReturnGatherZone.Instance != null && !ReturnGatherZone.Instance.AreAllAlivePlayersInside())
        {
            Debug.Log("[RoverManager] Return denied — not every player is in the return zone.");
            return;
        }

        StartCoroutine(ReturnSequence(teleportPos, teleportRot));
    }

    private IEnumerator ReturnSequence(Vector3 teleportPos, Quaternion teleportRot)
    {
        _returnSequenceRunning = true;

        // Completion fanfare first: sound + "EXPEDITION COMPLETE" UI on every client,
        // held for a few seconds before anyone moves.
        RpcExpeditionComplete(_completeUISeconds);
        yield return new WaitForSeconds(_completeUISeconds);

        if (QuotaManager.Instance != null)
        {
            // Cargo was already banked live in StoreCargo — just count the expedition
            // and evaluate the (already-accumulated) quota.
            QuotaManager.Instance.ServerRegisterExpeditionReturn();
            QuotaManager.Instance.ServerCheckQuotaAndProceed();
        }

        foreach (PlayerManager player in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
        {
            // Completing an expedition wipes the slate: dead players come back, and
            // health / oxygen / station charges reset to max. Runs BEFORE the teleport
            // so the revived player is moved to the lobby with everyone else.
            player.ServerResetForNewExpedition();

            player.TeleportToPosition(teleportPos, teleportRot);
            player.SetInsideDungeon(false); // back in the lobby — hide from the Echo
        }

        // Stagger drops so they don't spawn on top of each other and explode.
        for (int i = 0; i < _energyCells; i++)
            Instantiate(_energyCellPrefab, _lobbyDropPoint.position + Vector3.up * (0.4f * i), _lobbyDropPoint.rotation);

        _energyCells = 0;
        LastReturnTime = Time.time; // the start zone re-arms off this
        _returnSequenceRunning = false;
    }

    // Completion presentation on every client: sound + splash UI.
    [ObserversRpc(runLocally: true)]
    private void RpcExpeditionComplete(float uiSeconds)
    {
        PlayUISound(_expeditionCompleteSound);
        ExpeditionCompleteUI.Instance?.Show(uiSeconds);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Plays a flat 2D one-shot (UI/stinger sound, not positional).
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
