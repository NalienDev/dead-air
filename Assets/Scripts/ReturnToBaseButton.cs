using PurrNet;
using UnityEngine;

/// <summary>
/// Place this on the rover prefab alongside NetworkedSceneButton.
/// When the player interacts with it in the game scene:
///   1. Tallies cargo from RoverManager.
///   2. Submits items to QuotaManager.
///   3. QuotaManager decides the destination (lobby = quota met, game over = quota failed)
///      and triggers the scene change itself via ServerCheckQuotaAndProceed.
///
/// No scene name needed here — QuotaManager owns that decision.
/// </summary>
public class ReturnToBaseButton : Interactable
{
    public override InteractionType OnInteract(GameObject user)
    {
        if (RoverManager.Instance == null)
        {
            Debug.LogWarning("[ReturnToBaseButton] RoverManager.Instance is null.");
            return InteractionType.NONE;
        }

        if (QuotaManager.Instance == null)
        {
            Debug.LogWarning("[ReturnToBaseButton] QuotaManager.Instance is null.");
            return InteractionType.NONE;
        }

        RoverManager.Instance.GetCargoValues(out int bandwidth, out int energyCells);

        Debug.Log($"[ReturnToBaseButton] Submitting cargo — bandwidth: {bandwidth}, energy cells: {energyCells}");

        // Submit items, then evaluate quota. QuotaManager.ServerCheckQuotaAndProceed
        // handles the scene transition (lobby or game over) internally.
        QuotaManager.Instance.ServerProcessItems(bandwidth, energyCells);
        QuotaManager.Instance.ServerCheckQuotaAndProceed();

        return InteractionType.PRESS;
    }
}
