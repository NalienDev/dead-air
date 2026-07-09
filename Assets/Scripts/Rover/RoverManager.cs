using PurrNet;
using UnityEngine;

public class RoverManager : NetworkBehaviour
{
    public static RoverManager Instance { get; private set; }

    [SerializeField] private Transform _lobbyDropPoint;
    [SerializeField] private GameObject _energyCellPrefab;

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

    [ServerRpc(requireOwnership: false)]
    public void ServerRequestReturnToBase(Vector3 teleportPos, Quaternion teleportRot)
    {
        if (QuotaManager.Instance != null)
        {
            // Cargo was already banked live in StoreCargo — just count the expedition
            // and evaluate the (already-accumulated) quota.
            QuotaManager.Instance.ServerRegisterExpeditionReturn();
            QuotaManager.Instance.ServerCheckQuotaAndProceed();
        }

        foreach (PlayerManager player in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
        {
            player.TeleportToPosition(teleportPos, teleportRot);
            player.SetInsideDungeon(false); // back in the lobby — hide from the Echo
        }

        // Stagger drops so they don't spawn on top of each other and explode.
        for (int i = 0; i < _energyCells; i++)
            Instantiate(_energyCellPrefab, _lobbyDropPoint.position + Vector3.up * (0.4f * i), _lobbyDropPoint.rotation);

        _energyCells = 0;
    }
}
