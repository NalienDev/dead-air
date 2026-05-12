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

    public void ServerProcessItems(int bandwidth, int energyCells)
    {
        sessionBandwidth.value += bandwidth;
        currentEnergyCells.value += energyCells;

        Debug.Log($"[QuotaManager] +{bandwidth} bandwidth, +{energyCells} energy cells. " +
                  $"Session: {sessionBandwidth.value}/{currentQuota.value}");
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
            SceneChanger.Instance.LoadSceneForEveryone(_lobbySceneName);
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

        Debug.Log("[QuotaManager] Game reset.");
        SceneChanger.Instance.LoadSceneForEveryone(_lobbySceneName);
    }
}