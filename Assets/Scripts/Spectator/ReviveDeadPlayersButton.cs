using UnityEngine;

/// <summary>
/// Interactable button that revives every dead player. Meant to be pressed by a
/// living player to bring their dead teammates back.
///
/// Revival is server-authoritative — this just routes each dead player through
/// their own <see cref="PlayerDeathHandler.Revive"/>.
/// </summary>
public class ReviveDeadPlayersButton : Interactable
{
    [Tooltip("Health restored to each revived player.")]
    [SerializeField] private int _restoreHealth = 100;

    [Tooltip("Oxygen restored to each revived player.")]
    [SerializeField] private int _restoreOxygen = 360;

    public override InteractionType OnInteract(GameObject user)
    {
        PlayerManager[] players = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);

        int revivedCount = 0;
        foreach (PlayerManager player in players)
        {
            if (!player.IsDead) continue;

            PlayerDeathHandler deathHandler = player.GetComponent<PlayerDeathHandler>();
            if (deathHandler == null) continue;

            deathHandler.Revive(_restoreHealth, _restoreOxygen);
            revivedCount++;
        }

        Debug.Log($"[ReviveDeadPlayersButton] Revived {revivedCount} player(s).");
        return InteractionType.PRESS;
    }
}
