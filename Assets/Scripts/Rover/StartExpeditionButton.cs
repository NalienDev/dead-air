using PurrNet;
using UnityEngine;

/// <summary>
/// Place this button in the lobby rover. When interacted with, it starts the day
/// and teleports all players to a designated spawn point inside the expedition map.
/// If the dungeon hasn't finished generating yet, it shows a loading screen for
/// everyone and waits until generation is complete before teleporting.
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

        // If the dungeon isn't ready yet, show a loading screen and wait for generation.
        if (DungeonGenerator.Instance != null && !DungeonGenerator.Instance.IsGenerated())
        {
            Debug.Log("[StartExpeditionButton] Dungeon not generated yet — showing loading screen and waiting.");

            // Show the loading screen on all clients.
            if (SceneChanger.Instance != null)
                SceneChanger.Instance.RpcShowLoadingScreen();

            // Kick off generation if it hasn't started.
            DungeonGenerator.Instance.StartGeneration();

            // Subscribe — teleport happens once generation fires the event.
            DungeonGenerator.Instance.OnGenerated += OnDungeonGenerated;

            return InteractionType.PRESS;
        }

        // Dungeon already generated — teleport everyone immediately.
        TeleportAndHide();
        return InteractionType.PRESS;
    }
    private void TeleportPlayers()
    {
        Debug.Log("[StartExpeditionButton] Teleporting players to the expedition area.");
        
        if (PlayerManager.Local != null)
        {
            PlayerManager.Local.RequestTeleportAllPlayers(_expeditionSpawnPoint.position, _expeditionSpawnPoint.rotation);
        }
    }

    private void TeleportAndHide()
    {
        TeleportPlayers();

        // Hide the loading screen for all clients.
        if (SceneChanger.Instance != null)
            SceneChanger.Instance.RpcHideLoadingScreen();
    }

    private void OnDungeonGenerated()
    {
        Debug.Log("[StartExpeditionButton] Dungeon generation finished! Teleporting players.");

        // Unsubscribe first to prevent duplicate calls.
        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.OnGenerated -= OnDungeonGenerated;

        TeleportAndHide();
    }
}
