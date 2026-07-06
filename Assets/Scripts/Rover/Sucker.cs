using PurrNet;
using UnityEngine;

// Trigger volume on a rover. While active, cargo that reaches it is stored in the RoverManager.
public class Sucker : NetworkBehaviour
{
    [SerializeField] private SuctionZone _suctionZone;
    private SyncVar<bool> _canSuck = new SyncVar<bool>(false);

    protected override void OnSpawned(bool asServer)
    {
        _canSuck.onChanged += SetZoneActive;
        SetZoneActive(_canSuck.value);
    }

    protected override void OnDespawned(bool asServer) => _canSuck.onChanged -= SetZoneActive;

    public bool CanSuck() => _canSuck.value;

    public void SetCanSuck(bool value)
    {
        if (isServer) _canSuck.value = value;
        else ServerSetCanSuck(value);
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerSetCanSuck(bool value) => _canSuck.value = value;

    private void SetZoneActive(bool value)
    {
        if (_suctionZone != null) _suctionZone.gameObject.SetActive(value);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isServer || !_canSuck.value) return;
        if (other.CompareTag("Player")) return;

        if (other.TryGetComponent(out NetworkIdentity identity))
            RoverManager.Instance.StoreCargo(identity);
    }
}
