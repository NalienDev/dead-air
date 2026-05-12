using UnityEngine;

/// <summary>
/// Toggles the Sucker on/off via RoverManager.
/// Uses RoverManager.Instance.Sucker instead of FindFirstObjectByType
/// since the Sucker is recreated each scene.
/// </summary>
public class ActivateVacuumButton : Interactable
{
    public override InteractionType OnInteract(GameObject user)
    {
        if (RoverManager.Instance == null)
        {
            Debug.LogWarning("[ActivateVacuumButton] RoverManager.Instance is null.");
            return InteractionType.NONE;
        }

        Sucker sucker = RoverManager.Instance.Sucker;
        if (sucker == null)
        {
            Debug.LogWarning("[ActivateVacuumButton] No Sucker found on RoverManager.");
            return InteractionType.NONE;
        }

        sucker.SetCanSuck(!sucker.CanSuck());
        return InteractionType.PRESS;
    }
}