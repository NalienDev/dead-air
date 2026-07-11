using PurrNet;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interactable button that returns the team to the lobby once everyone is gathered.
/// </summary>
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

        // Locked for the first minute; this is just instant local feedback, the server re-validates.
        if (!RoverManager.Instance.CanReturnToBase)
        {
            Debug.Log("[ReturnToBaseButton] Locked, the expedition just started.");
            return InteractionType.NONE;
        }

        // Every alive player must be gathered in the return zone.
        if (ReturnGatherZone.Instance != null && !ReturnGatherZone.Instance.AreAllAlivePlayersInside())
        {
            Debug.Log("[ReturnToBaseButton] Denied, not every player is in the return zone.");
            return InteractionType.NONE;
        }

        RoverManager.Instance.ServerRequestReturnToBase(_lobbyTeleportPoint.position, _lobbyTeleportPoint.rotation);

        return InteractionType.PRESS;
    }
}
