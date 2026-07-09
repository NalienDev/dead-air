using PurrNet;
using UnityEngine;

/// <summary>
/// DDOL singleton tracking quota state.
/// Uses NetworkIdentity for SyncVar replication to clients.
///
/// IMPORTANT: Do NOT use [ServerRpc] on this class. This object is moved to
/// DontDestroyOnLoad in Awake() — outside PurrNet's spawn pipeline — so
/// PurrNet considers it "not spawned" and rejects all RPCs on it.
///
/// Instead, call these methods only from server/host context (e.g. from
/// ReturnToBaseButton which runs on the interacting player's machine).
/// On a host, networkManager.isServer is true. On a dedicated server,
/// same applies. Pure clients should never call these directly — add a
/// [ServerRpc] on the caller side if pure-client support is needed later.
/// </summary>
public class QuotaManager : NetworkIdentity
{
    public static QuotaManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private int _baseQuota = 1000;
    [SerializeField] private float _quotaMultiplier = 1.3f;
    [SerializeField] private string _gameOverSceneName = "GameOver";

    [Header("Synced State")]
    public SyncVar<int> currentDay = new SyncVar<int>(1);
    public SyncVar<int> currentQuota = new SyncVar<int>(1000);
    public SyncVar<int> totalBandwidth = new SyncVar<int>(0);
    public SyncVar<int> sessionBandwidth = new SyncVar<int>(0);
    public SyncVar<int> currentEnergyCells = new SyncVar<int>(0);

    /// <summary>Completed expeditions (returns to base). Gates certain upgrades.</summary>
    public SyncVar<int> expeditionsCompleted = new SyncVar<int>(0);

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        
        if (asServer && currentQuota.value == 0) 
            currentQuota.value = _baseQuota;
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        if (Instance == this) Instance = null;
    }

    public void ServerProcessItems(int bandwidth, int energyCells)
    {
        sessionBandwidth.value += bandwidth;
        currentEnergyCells.value += energyCells;

        Debug.Log($"[QuotaManager] +{bandwidth} bandwidth, +{energyCells} energy cells. " +
                  $"Session: {sessionBandwidth.value}/{currentQuota.value}");
    }

    /// <summary>
    /// Server-side. Banks bandwidth the instant it's vacuumed up, so the quota UI ticks
    /// live during the expedition. The end-of-run quota check reads the same
    /// <see cref="sessionBandwidth"/>, so extraction is still calculated the same way.
    /// </summary>
    public void ServerAddBandwidth(int amount)
    {
        if (amount == 0) return;
        sessionBandwidth.value += amount;
    }

    /// <summary>Server-side. Counts energy cells live as they're vacuumed up.</summary>
    public void ServerAddEnergyCells(int amount)
    {
        if (amount == 0) return;
        currentEnergyCells.value += amount;
    }

    /// <summary>Server-side. One more completed expedition (called on return to base).</summary>
    public void ServerRegisterExpeditionReturn()
    {
        expeditionsCompleted.value++;
    }

    public void ServerCheckQuotaAndProceed()
    {
        if (sessionBandwidth.value >= currentQuota.value)
        {
            totalBandwidth.value += sessionBandwidth.value;
            sessionBandwidth.value = 0;
            currentDay.value++;
            currentQuota.value = Mathf.RoundToInt(currentQuota.value * _quotaMultiplier);

            Debug.Log("[QuotaManager] Quota met — advancing to next day.");
            // Teleportation to lobby is handled by ReturnToBaseButton now, no scene load needed.
            
            if (DungeonGenerator.Instance != null)
                DungeonGenerator.Instance.RegenerateDungeon();
        }
        else
        {
            Debug.Log("[QuotaManager] Quota NOT met — Game Over.");
            SceneChanger.Instance.LoadSceneForEveryone(_gameOverSceneName);
        }
    }

    public void ServerSpendBandwidth(int amount)
    {
        if (totalBandwidth.value >= amount)
            totalBandwidth.value -= amount;
    }

    public void ServerUseEnergyCell()
    {
        if (currentEnergyCells.value > 0)
            currentEnergyCells.value--;
    }

    public void ServerResetGame()
    {
        currentDay.value = 1;
        currentQuota.value = _baseQuota;
        totalBandwidth.value = 0;
        sessionBandwidth.value = 0;
        currentEnergyCells.value = 0;
        expeditionsCompleted.value = 0;

        Debug.Log("[QuotaManager] Game reset.");
        // Resetting game doesn't need to load the lobby scene, as we teleport back to the lobby point.
        // If a specific reset sequence is needed, handle it here.
        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.RegenerateDungeon();
    }
}