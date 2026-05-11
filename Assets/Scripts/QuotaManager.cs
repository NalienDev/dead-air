using PurrNet;
using UnityEngine;

public class QuotaManager : NetworkIdentity
{
    public static QuotaManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private int _baseQuota = 1000;
    [SerializeField] private float _quotaMultiplier = 1.3f;
    [SerializeField] private string _gameOverSceneName = "GameOver";
    [SerializeField] private string _lobbySceneName = "GameLobby";

    [Header("Synced State")]
    public SyncVar<int> currentDay = new SyncVar<int>(1);
    public SyncVar<int> currentQuota = new SyncVar<int>(1000);
    public SyncVar<int> totalBandwidth = new SyncVar<int>(0);
    public SyncVar<int> sessionBandwidth = new SyncVar<int>(0);
    public SyncVar<int> currentEnergyCells = new SyncVar<int>(0);

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        if (currentQuota.value == 0) currentQuota.value = _baseQuota;
    }

    [ServerRpc(requireOwnership: false)]
    public void ServerProcessItems(int bandwidth, int energyCells)
    {
        sessionBandwidth.value += bandwidth;
        currentEnergyCells.value += energyCells;
        
        Debug.Log($"[QuotaManager] Processed items: +{bandwidth} bandwidth, +{energyCells} energy cells. Session Total: {sessionBandwidth.value}/{currentQuota.value}");
        
        // After processing, check if this was a return to lobby and if we should evaluate the day
        // The user says: "returning from testlevel, theyll go to another scene... to indicate game over"
        // This suggests the check happens on return.
    }

    [ServerRpc(requireOwnership: false)]
    public void ServerCheckQuotaAndProceed()
    {
        if (sessionBandwidth.value >= currentQuota.value)
        {
            // Quota met! Bank the collected bandwidth
            totalBandwidth.value += sessionBandwidth.value;
            sessionBandwidth.value = 0;
            
            currentDay.value++;
            currentQuota.value = Mathf.RoundToInt(currentQuota.value * _quotaMultiplier);
            
            Debug.Log("[QuotaManager] Quota met! Proceeding to next day upgrades.");
        }
        else
        {
            // Quota NOT met! Game Over
            Debug.Log("[QuotaManager] Quota NOT met! Game Over.");
            SceneChanger.Instance.LoadSceneForEveryone(_gameOverSceneName);
        }
    }

    [ServerRpc(requireOwnership: false)]
    public void ServerSpendBandwidth(int amount)
    {
        if (totalBandwidth.value >= amount)
        {
            totalBandwidth.value -= amount;
        }
    }

    [ServerRpc(requireOwnership: false)]
    public void ServerUseEnergyCell()
    {
        if (currentEnergyCells.value > 0)
        {
            currentEnergyCells.value--;
        }
    }

    [ServerRpc(requireOwnership: false)]
    public void ServerResetGame()
    {
        currentDay.value = 1;
        currentQuota.value = _baseQuota;
        totalBandwidth.value = 0;
        sessionBandwidth.value = 0;
        currentEnergyCells.value = 0;
        
        Debug.Log("[QuotaManager] Game Reset.");
        SceneChanger.Instance.LoadSceneForEveryone(_lobbySceneName);
    }
}
