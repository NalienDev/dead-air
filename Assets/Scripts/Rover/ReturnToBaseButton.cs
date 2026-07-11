using PurrNet;
using System.Collections.Generic;
using UnityEngine;

public class ReturnToBaseButton : Interactable
{
    private Transform _lobbyTeleportPoint;

    private void Start()
    {
        GameObject[] teleportOptions = GameObject.FindGameObjectsWithTag("SpawnLocation");
        int rnd =  Random.Range(0, teleportOptions.Length);
        _lobbyTeleportPoint = teleportOptions[rnd].transform;
    }

    public override InteractionType OnInteract(GameObject user)
    {
        if (RoverManager.Instance == null)
        {
            Debug.LogWarning("[ReturnToBaseButton] RoverManager.Instance is null.");
            return InteractionType.NONE;
        }

        // Locked for the first minute of the expedition (synced flag; the server
        // re-validates too, this is just instant local feedback).
        if (!RoverManager.Instance.CanReturnToBase)
        {
            Debug.Log("[ReturnToBaseButton] Locked — the expedition just started.");
            return InteractionType.NONE;
        }

        // Everyone (alive) must be gathered in the return zone. Positions are
        // replicated, so this local check matches the server's re-validation.
        if (ReturnGatherZone.Instance != null && !ReturnGatherZone.Instance.AreAllAlivePlayersInside())
        {
            Debug.Log("[ReturnToBaseButton] Denied — not every player is in the return zone.");
            return InteractionType.NONE;
        }

        // Submit items, evaluate quota, and teleport players securely on the server.
        RoverManager.Instance.ServerRequestReturnToBase(_lobbyTeleportPoint.position, _lobbyTeleportPoint.rotation);

        return InteractionType.PRESS;
    }
}
