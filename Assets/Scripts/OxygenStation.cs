using PurrNet;
using UnityEngine;

/// <summary>
/// Station that refills a player's oxygen on interact, spending one of their per-player charges.
/// </summary>
public class OxygenStation : Interactable
{
    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Played at the station when a refill succeeds.")]
    [SerializeField] private AudioClip _rechargeSound;
    [Tooltip("Played for the interacting player when out of charges or already full.")]
    [SerializeField] private AudioClip _denySound;

    [Header("Visuals")]
    [Tooltip("Shown while the local player has no charges left.")]
    [SerializeField] private GameObject _noChargesVisual;

    private void Update()
    {
        if (_noChargesVisual == null) return;

        PlayerManager local = PlayerManager.Local;
        bool exhausted = local != null && local.currentOxygenCharges.value <= 0;
        if (_noChargesVisual.activeSelf != exhausted)
            _noChargesVisual.SetActive(exhausted);
    }

    public override InteractionType OnInteract(GameObject user)
    {
        PlayerManager pm = user.GetComponent<PlayerManager>()
                           ?? user.GetComponentInParent<PlayerManager>();
        if (pm == null)
        {
            Debug.LogWarning("[OxygenStation] Interacting object has no PlayerManager.", this);
            return InteractionType.NONE;
        }

        // Local pre-check for instant feedback; the server re-validates anyway.
        if (pm.currentOxygenCharges.value <= 0 ||
            pm.GetCurrentOxygen() >= pm.GetMaxOxygen())
        {
            PlayLocal(_denySound);
            return InteractionType.PRESS;
        }

        ServerUseStation(pm);
        return InteractionType.PRESS;
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerUseStation(PlayerManager pm)
    {
        if (pm == null) return;

        if (pm.ServerTryUseOxygenCharge())
            RpcPlayRecharge();
        else if (pm.owner.HasValue)
            TargetDeny(pm.owner.Value);
    }

    [ObserversRpc(runLocally: true)]
    private void RpcPlayRecharge() => PlayLocal(_rechargeSound);

    [TargetRpc]
    private void TargetDeny(PlayerID target) => PlayLocal(_denySound);

    private void PlayLocal(AudioClip clip)
    {
        if (clip == null) return;
        if (_audioSource != null) _audioSource.PlayOneShot(clip);
        else AudioSource.PlayClipAtPoint(clip, transform.position);
    }
}
