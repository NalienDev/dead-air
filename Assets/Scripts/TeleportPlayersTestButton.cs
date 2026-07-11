using PurrNet;
using UnityEngine;

/// <summary>
/// Test button that teleports every player to a target transform once the dungeon is generated.
/// </summary>
public class TeleportPlayersTestButton : Interactable
{
    [SerializeField] private Transform _teleportTransform;

    public override InteractionType OnInteract(GameObject user)
    {
        if (!DungeonGenerator.Instance.IsGenerated())
        {
            Debug.Log("[TeleportPlayersTestButton] Dungeon is still generating, please wait.");
            return InteractionType.NONE;
        }

        // Only the server can authoritatively move players.
        PlayerManager presser = user.GetComponent<PlayerManager>();
        if (presser != null)
            presser.RequestTeleportAllPlayers(_teleportTransform.position, _teleportTransform.rotation);

        return InteractionType.PRESS;
    }
}
