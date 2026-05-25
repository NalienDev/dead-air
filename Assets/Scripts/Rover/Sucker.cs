using PurrNet;
using UnityEngine;

/// <summary>
/// Trigger volume on the Rover prefab.
/// Stateless — delegates all cargo storage to RoverManager.
/// Starts INACTIVE every scene; enabled via ActivateVacuumButton.
/// </summary>
public class Sucker : MonoBehaviour
{
    private RoverManager _roverManager;
    private SuctionZone _suctionZone;
    private bool _canSuck;

    private void Awake()
    {
        _suctionZone = GetComponentInChildren<SuctionZone>(includeInactive: true);
        // Always start off — ActivateVacuumButton enables it explicitly.
        SetCanSuck(false);
    }

    public void Initialise(RoverManager roverManager)
    {
        _roverManager = roverManager;
    }

    public bool CanSuck() => _canSuck;

    public void SetCanSuck(bool value)
    {
        _canSuck = value;
        if (_suctionZone != null)
            _suctionZone.gameObject.SetActive(_canSuck);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_canSuck) return;
        if (_roverManager == null)
        {
            Debug.LogWarning("[Sucker] Not initialised — call Initialise() first.");
            return;
        }

        // Require a NetworkIdentity — but do NOT check isSpawned.
        // Objects moved to DDOL lose their PurrNet spawn state, so that check
        // would silently reject everything after the first scene transition.
        if (!other.TryGetComponent(out NetworkIdentity identity)) return;

        if (other.TryGetComponent(out SuckableObject suckable))
            suckable.EndAttraction();

        // Keep the object alive across scene transitions.
        DontDestroyOnLoad(identity.gameObject);

        _roverManager.AddCargo(identity);
        identity.gameObject.SetActive(false);
    }
}