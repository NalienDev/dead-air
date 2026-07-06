using PurrNet;
using UnityEngine;

public class RoverManager : NetworkBehaviour
{
    public static RoverManager Instance { get; private set; }

    [SerializeField] private Transform _lobbyDropPoint;
    [SerializeField] private GameObject _energyCellPrefab;

    private int _energyCells;
    private int _bandwidth;

    protected override void OnSpawned(bool asServer) => Instance = this;

    protected override void OnDespawned(bool asServer)
    {
        if (Instance == this) Instance = null;
    }

    // Server: called when the expedition sucker pulls something in. Bandwidth is
    // tallied for the quota then discarded; energy cells are respawned in the lobby.
    public void StoreCargo(NetworkIdentity identity)
    {
        if (identity.TryGetComponent(out BandwidthObject bw))
            _bandwidth += bw.BandwidthValue;
        else if (identity.TryGetComponent(out EnergyCell _))
            _energyCells++;
        else
            return;

        Destroy(identity.gameObject);
    }

    [ServerRpc(requireOwnership: false)]
    public void ServerRequestReturnToBase(Vector3 teleportPos, Quaternion teleportRot)
    {
        if (QuotaManager.Instance != null)
        {
            QuotaManager.Instance.ServerProcessItems(_bandwidth, _energyCells);
            QuotaManager.Instance.ServerCheckQuotaAndProceed();
        }

        foreach (PlayerManager player in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
            player.TeleportToPosition(teleportPos, teleportRot);

        // Stagger drops so they don't spawn on top of each other and explode.
        for (int i = 0; i < _energyCells; i++)
            Instantiate(_energyCellPrefab, _lobbyDropPoint.position + Vector3.up * (0.4f * i), _lobbyDropPoint.rotation);

        _energyCells = 0;
        _bandwidth = 0;
    }
}
