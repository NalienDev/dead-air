using PurrNet;
using UnityEngine;

/// <summary>
/// Trigger volume on the Rover prefab.
/// Stateless — delegates all cargo storage to RoverManager.
/// Starts INACTIVE every scene; enabled via ActivateVacuumButton.
/// </summary>
public class Sucker : NetworkBehaviour
{
    private RoverManager _roverManager;
    [SerializeField]private SuctionZone _suctionZone;
    private SyncVar<bool> _canSuck = new SyncVar<bool>(false);

    private void Awake()
    {
        // Always start off — ActivateVacuumButton enables it explicitly.
        _canSuck.value = false;
        if (_suctionZone != null)
            _suctionZone.gameObject.SetActive(false);
    }

    public void Initialise(RoverManager roverManager)
    {
        _roverManager = roverManager;
    }

    public bool CanSuck() => _canSuck;
    public void SetCanSuck(bool value)
    {
        if (isServer)
        {
            ApplyCanSuck(value);
        }
        else
        {
            ServerSetCanSuck(value);
        }
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerSetCanSuck(bool value)
    {
        ApplyCanSuck(value);
    }

    private void ApplyCanSuck(bool value)
    {
        Debug.Log("Setting Sucker to " + value);
        _canSuck.value = value;
        if (_suctionZone != null)
            _suctionZone.gameObject.SetActive(value);
        
        RpcApplyCanSuck(value);
    }

    [ObserversRpc(runLocally: false)]
    private void RpcApplyCanSuck(bool value)
    {
        if (_suctionZone != null)
            _suctionZone.gameObject.SetActive(value);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isServer) return;
        if (!_canSuck.value) return;
        if (_roverManager == null)
        {
            Debug.LogWarning("[Sucker] Not initialised — call Initialise() first.");
            return;
        }

        if (other.gameObject.CompareTag("Player")) return;

        // Require a NetworkIdentity — but do NOT check isSpawned.
        // Objects moved to DDOL lose their PurrNet spawn state, so that check
        // would silently reject everything after the first scene transition.
        if (!other.TryGetComponent(out NetworkIdentity identity)) return;

        if (other.TryGetComponent(out SuckableObject suckable))
            suckable.EndAttraction();

        _roverManager.AddCargo(identity);
        RpcHideObject(identity);
    }

    [ObserversRpc(runLocally: true)]
    private void RpcHideObject(NetworkIdentity identity)
    {
        if (identity == null) return;
        
        // Keep the object alive across scene transitions.
        DontDestroyOnLoad(identity.gameObject);
        identity.gameObject.SetActive(false);
    }
}