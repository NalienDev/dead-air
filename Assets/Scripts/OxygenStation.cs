using PurrNet;
using UnityEngine;

/// <summary>
/// A wall/floor station that refills a player's oxygen to full when they press E on it
/// (routed through <see cref="Interactor"/>). Refills are limited by PER-PLAYER charges
/// (<see cref="PlayerManager.currentOxygenCharges"/>, 3 by default): every player has
/// their own pool, spendable at any station, and they reset to max when an expedition
/// completes (see <see cref="PlayerManager.ServerResetForNewExpedition"/>). The
/// +charge upgrade raises a player's max.
///
/// Server-authoritative: the interacting client asks, the server validates and spends
/// the charge. Sounds: the recharge hiss plays at the station for everyone nearby
/// (ObserversRpc); the deny buzz plays only for the rejected player. The optional
/// "no charges" visual reflects the LOCAL player's pool — each client sees the station
/// dead/alive according to their own remaining charges.
///
/// Put this on a networked scene object with a collider on the interactable layer, and
/// wire the AudioSource + clips + visual.
/// </summary>
public class OxygenStation : Interactable
{
    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Played at the station when a refill succeeds (everyone nearby hears it).")]
    [SerializeField] private AudioClip _rechargeSound;
    [Tooltip("Played for the interacting player when they're out of charges " +
             "(or already at full oxygen).")]
    [SerializeField] private AudioClip _denySound;

    [Header("Visuals")]
    [Tooltip("Toggled ON while the LOCAL player has no charges left (e.g. a dead screen " +
             "or red light). Per-client — each player sees their own state.")]
    [SerializeField] private GameObject _noChargesVisual;

    private void Update()
    {
        if (_noChargesVisual == null) return;

        PlayerManager local = PlayerManager.Local;
        bool exhausted = local != null && local.currentOxygenCharges.value <= 0;
        if (_noChargesVisual.activeSelf != exhausted)
            _noChargesVisual.SetActive(exhausted);
    }

    // ── Interaction (runs on the interacting client) ──────────────────────────

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

    // ── Server ────────────────────────────────────────────────────────────────

    [ServerRpc(requireOwnership: false)]
    private void ServerUseStation(PlayerManager pm)
    {
        if (pm == null) return;

        if (pm.ServerTryUseOxygenCharge())
            RpcPlayRecharge();
        else if (pm.owner.HasValue)
            TargetDeny(pm.owner.Value);
    }

    // ── Presentation ─────────────────────────────────────────────────────────

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
