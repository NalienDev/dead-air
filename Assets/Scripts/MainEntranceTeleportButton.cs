using UnityEngine;

/// <summary>
/// Interactable button that teleports the user to the main entrance spawn point.
/// </summary>
public class MainEntranceTeleportButton : Interactable
{
    public override InteractionType OnInteract(GameObject user)
    {
        Transform teleportLocation = FindFirstObjectByType<MainEntrance>().getSpawnPoint();

        user.transform.position = teleportLocation.position;
        return InteractionType.PRESS;
    }
}
