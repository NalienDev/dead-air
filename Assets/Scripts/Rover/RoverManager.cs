using System.Collections;
using PurrNet;
using UnityEngine;

public class RoverManager : NetworkBehaviour
{
    public static RoverManager Instance { get; private set; }

    [SerializeField] private Transform _lobbyDropPoint;
    [SerializeField] private GameObject _energyCellPrefab;

    [Tooltip("Seconds after an expedition starts before the return-to-base button works.")]
    [SerializeField] private float _returnLockSeconds = 60f;

    // False while the early-return lock is armed. Replicated so every client's
    // ReturnToBaseButton agrees; the server also re-validates on use.
    private readonly SyncVar<bool> _canReturnToBase = new(true);

    private Coroutine _unlockRoutine;

    /// <summary>Whether the return-to-base button currently works.</summary>
    public bool CanReturnToBase => _canReturnToBase.value;

    private int _energyCells;

    protected override void OnSpawned(bool asServer) => Instance = this;

    protected override void OnDespawned(bool asServer)
    {
        if (Instance == this) Instance = null;
    }

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

    // ── Expedition start (server-driven) ─────────────────────────────────────
    //
    // The WHOLE start flow must run on the server: StartGeneration() is a no-op on
    // clients and OnGenerated only ever fires server-side. The old client-side flow
    // (in StartExpeditionButton) silently did nothing when a non-host pressed the
    // button — the loading screen appeared locally and never went away because no
    // generation was actually started.

    private Transform _expeditionSpawnPoint;

    /// <summary>
    /// Any client presses the rover button → the server runs the expedition start:
    /// generate the dungeon if needed (loading screen up for everyone while it runs),
    /// then teleport all players in and arm the early-return lock.
    /// </summary>
    [ServerRpc(requireOwnership: false)]
    public void ServerStartExpedition()
    {
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

        DungeonGenerator gen = DungeonGenerator.Instance;
        if (gen != null && !gen.IsGenerated())
        {
            Debug.Log("[RoverManager] Dungeon not generated — showing loading screen and waiting.");
            SceneChanger.Instance?.RpcShowLoadingScreen();

            // -= before += so a double press never stacks a second teleport.
            gen.OnGenerated -= OnDungeonGeneratedTeleport;
            gen.OnGenerated += OnDungeonGeneratedTeleport;
            gen.StartGeneration();
            return;
        }

        TeleportPlayersToExpedition();
    }

    private void OnDungeonGeneratedTeleport()
    {
        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.OnGenerated -= OnDungeonGeneratedTeleport;

        TeleportPlayersToExpedition();
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

    [ServerRpc(requireOwnership: false)]
    public void ServerRequestReturnToBase(Vector3 teleportPos, Quaternion teleportRot)
    {
        // Server-side re-validation of the early-return lock — the client-side check
        // in ReturnToBaseButton is only for instant feedback.
        if (!_canReturnToBase.value)
        {
            Debug.Log("[RoverManager] Return-to-base denied — expedition just started.");
            return;
        }

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
    }
}
