using PurrNet;
using UnityEngine;

/// <summary>
/// Place this button in the lobby rover. When interacted with, it starts the day
/// and teleports all players to a designated spawn point inside the expedition map.
/// </summary>
public class StartExpeditionButton : Interactable
{
    [Tooltip("The Transform inside the actual map where players should be teleported when starting the expedition.")]
    [SerializeField] private Transform _expeditionSpawnPoint;

    public override InteractionType OnInteract(GameObject user)
    {
        if (_expeditionSpawnPoint == null)
        {
            Debug.LogWarning("[StartExpeditionButton] Expedition spawn point is not assigned!");
            return InteractionType.NONE;
        }

        Debug.Log("[StartExpeditionButton] Starting the day! Teleporting players to the expedition area.");

        // Find all active players and teleport them to the expedition start location
        PlayerManager[] players = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);
        foreach (var player in players)
        {
            player.transform.SetPositionAndRotation(_expeditionSpawnPoint.position, _expeditionSpawnPoint.rotation);
        }

        return InteractionType.PRESS;
    }
}
