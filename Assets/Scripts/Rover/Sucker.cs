using PurrNet;
using UnityEngine;

/// <summary>
/// Rover intake that, while active, stores any cargo reaching its trigger in the RoverManager.
/// </summary>
public class Sucker : NetworkBehaviour
{
    [SerializeField] private SuctionZone _suctionZone;
    [SerializeField] private Animator _leverAnimator;
    [SerializeField] private string _animatorParameterName = "isPulled";
    [SerializeField] private float _vacuumDuration = 3f;
    
    private SyncVar<bool> _canSuck = new SyncVar<bool>(false);
    private Coroutine _timerCoroutine;

    protected override void OnSpawned(bool asServer)
    {
        _canSuck.onChanged += SetZoneActive;
        SetZoneActive(_canSuck.value);
    }

    protected override void OnDespawned(bool asServer)
    {
        _canSuck.onChanged -= SetZoneActive;
        if (asServer && _timerCoroutine != null)
        {
            StopCoroutine(_timerCoroutine);
            _timerCoroutine = null;
        }
    }

    public bool CanSuck() => _canSuck.value;

    public void ActivateVacuum()
    {
        if (isServer) ServerActivateVacuum();
        else ServerRequestActivate();
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerRequestActivate() => ServerActivateVacuum();

    private void ServerActivateVacuum()
    {
        if (!isServer) return;
        if (_timerCoroutine != null) StopCoroutine(_timerCoroutine);
        _canSuck.value = true;
        _timerCoroutine = StartCoroutine(VacuumTimerRoutine());
    }

    private System.Collections.IEnumerator VacuumTimerRoutine()
    {
        yield return new WaitForSeconds(_vacuumDuration);
        _canSuck.value = false;
        _timerCoroutine = null;
    }

    private void SetZoneActive(bool value)
    {
        if (_suctionZone != null) _suctionZone.gameObject.SetActive(value);
        if (_leverAnimator != null)
        {
            _leverAnimator.SetBool(_animatorParameterName, value);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isServer || !_canSuck.value) return;
        if (other.CompareTag("Player")) return;

        if (other.TryGetComponent(out NetworkIdentity identity))
            RoverManager.Instance.StoreCargo(identity);
    }
}
