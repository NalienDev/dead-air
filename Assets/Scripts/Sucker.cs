using PurrNet;
using UnityEngine;

/// <summary>
/// Trigger volume that collects SuckableObjects and hands them off to
/// RoverManager for cross-scene persistence.
/// Sucker itself holds NO networked state — it is a scene-bound object
/// that is destroyed and recreated on every scene load.
/// </summary>
public class Sucker : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────
    [SerializeField] private Transform _objectSpawnTransform;

    // ── Private State ──────────────────────────────────────────────────────
    private RoverManager _roverManager;
    private SuctionZone _suctionZone;
    private bool _canSuck;

    // ── Initialisation ─────────────────────────────────────────────────────
    private void Awake()
    {
        _suctionZone = GetComponentInChildren<SuctionZone>(includeInactive: true);
    }

    /// <summary>
    /// Called by RoverManager immediately after instantiating the rover prefab.
    /// Provides the authoritative storage back-reference.
    /// </summary>
    public void Initialise(RoverManager roverManager)
    {
        _roverManager = roverManager;
    }

    // ── Public API ─────────────────────────────────────────────────────────
    public Transform ObjectSpawnTransform => _objectSpawnTransform;

    public bool CanSuck()
    {
        return _canSuck;
    }

    public void SetCanSuck(bool value)
    {
        _canSuck = value;

        if (_suctionZone != null)
            _suctionZone.gameObject.SetActive(_canSuck);
    }

    // ── Trigger ────────────────────────────────────────────────────────────
    private void OnTriggerEnter(Collider other)
    {
        if (!_canSuck) return;
        if (_roverManager == null)
        {
            Debug.LogWarning("[Sucker] No RoverManager reference — call Initialise() first.");
            return;
        }

        if (!other.TryGetComponent(out NetworkIdentity identity)) return;
        if (!identity.isSpawned) return;

        // Stop attraction VFX before hiding the object.
        if (other.TryGetComponent(out SuckableObject suckable))
            suckable.EndAttraction();

        // Move to DDOL so the GameObject survives scene transitions.
        DontDestroyOnLoad(identity.gameObject);

        // Hand off to RoverManager — it owns the list.
        _roverManager.AddCargo(identity);

        identity.gameObject.SetActive(false);
    }
}