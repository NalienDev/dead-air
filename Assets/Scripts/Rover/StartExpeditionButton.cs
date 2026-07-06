using PurrNet;
using UnityEngine;


public class StartExpeditionButton : Interactable
{
    private Transform _expeditionSpawnPoint;

    private void Start()
    {
        _expeditionSpawnPoint = GameObject.FindGameObjectWithTag("ExpeditionSpawn").transform;
    }
    public override InteractionType OnInteract(GameObject user)
    {
        if (_expeditionSpawnPoint == null)
        {
            _expeditionSpawnPoint = GameObject.FindGameObjectWithTag("ExpeditionSpawn").transform;
            if (_expeditionSpawnPoint == null)
            {
                Debug.LogWarning("[StartExpeditionButton] Expedition spawn point is not assigned!");
                return InteractionType.NONE;
            }
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
