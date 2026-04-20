using UnityEngine;

public class MainEntranceTeleportButton : Interactable
{
    public override InteractionType OnInteract(GameObject user)
    {
        Transform teleportLocation = FindFirstObjectByType<MainEntrance>().getSpawnPoint();

        user.transform.position = teleportLocation.position;
        return InteractionType.PRESS;
    }
}
