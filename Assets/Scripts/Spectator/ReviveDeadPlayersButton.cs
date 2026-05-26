using PurrNet;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEditor.PlayerSettings;

/// <summary>
/// Interactable button that revives all dead players.

/// If no spawn location is assigned, players are revived in-place.
/// Assign a Transform in the Inspector, or drop a GameObject with a
/// clearly-named empty Transform (e.g. "ReviveSpawnPoint") next to the button.
/// </summary>
public class ReviveDeadPlayersButton : Interactable
{
    // ── Inspector ──────────────────────────────────────────────────────────
    [Tooltip("Health restored to each revived player.")]
    [SerializeField] private int _restoreHealth = 100;

    [Tooltip("Oxygen restored to each revived player.")]
    [SerializeField] private int _restoreOxygen = 360;

    // ── Interactable ───────────────────────────────────────────────────────

    public override InteractionType OnInteract(GameObject user)
    {
        PlayerManager[] players = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);

        int revivedCount = 0;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerManager player = players[i];
            if (!player.IsDead) continue;

            // Revive stats via server-authoritative path
            PlayerDeathHandler deathHandler = player.GetComponent<PlayerDeathHandler>();
            if (deathHandler == null) continue;

            deathHandler.Revive(_restoreHealth, _restoreOxygen);

            revivedCount++;
        }

        Debug.Log($"[ReviveDeadPlayersButton] Revived {revivedCount} player(s).");
        return InteractionType.PRESS;
    }

}