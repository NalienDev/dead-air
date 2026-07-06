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

        // Submit items, evaluate quota, and teleport players securely on the server.
        RoverManager.Instance.ServerRequestReturnToBase(_lobbyTeleportPoint.position, _lobbyTeleportPoint.rotation);

        return InteractionType.PRESS;
    }
}
