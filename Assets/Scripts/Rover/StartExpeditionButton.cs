using PurrNet;
using UnityEngine;

/// <summary>
/// Rover button that starts the expedition. All the real work (generating the dungeon,
/// the loading screen, teleporting everyone, arming the return lock) happens on the
/// SERVER inside <see cref="RoverManager.ServerStartExpedition"/> — generation is
/// server-only and its OnGenerated event never fires on clients, so a client-side flow
/// here silently hangs when a non-host presses the button (loading screen forever, no
/// teleport). This just forwards the press.
/// </summary>
public class StartExpeditionButton : Interactable
{
    public override InteractionType OnInteract(GameObject user)
    {
        if (RoverManager.Instance == null)
        {
            Debug.LogWarning("[StartExpeditionButton] RoverManager.Instance is null.");
            return InteractionType.NONE;
        }

        RoverManager.Instance.ServerStartExpedition();
        return InteractionType.PRESS;
    }
}
