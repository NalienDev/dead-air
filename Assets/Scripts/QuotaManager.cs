using PurrNet;
using UnityEngine;

/// <summary>
/// Persistent singleton tracking quota, bandwidth, and day progression via SyncVars.
/// </summary>
// Don't add [ServerRpc] here: this object lives in DontDestroyOnLoad, outside PurrNet's
// spawn pipeline, so RPCs are rejected. Call these methods from server/host context only.
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

    // Completed expeditions; gates certain upgrades.
    public SyncVar<int> expeditionsCompleted = new SyncVar<int>(0);

    // Why the run last ended in a game over; see GameOverReason.
    public SyncVar<int> lastGameOverReason = new SyncVar<int>(GameOverReason.None);

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

    // Banks bandwidth as it's vacuumed up so the quota UI ticks live during the expedition.
    public void ServerAddBandwidth(int amount)
    {
        if (amount == 0) return;
        sessionBandwidth.value += amount;
    }

    public void ServerAddEnergyCells(int amount)
    {
        if (amount == 0) return;
        currentEnergyCells.value += amount;
    }

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
            // Return to lobby is handled by ReturnToBaseButton, so no scene load here.
            if (DungeonGenerator.Instance != null)
                DungeonGenerator.Instance.RegenerateDungeon();
        }
        else
        {
            Debug.Log("[QuotaManager] Quota NOT met — Game Over.");
            lastGameOverReason.value = GameOverReason.QuotaNotMet;
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
        lastGameOverReason.value = GameOverReason.None;

        Debug.Log("[QuotaManager] Game reset.");
        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.RegenerateDungeon();
    }
}