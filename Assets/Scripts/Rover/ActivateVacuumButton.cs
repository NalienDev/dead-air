using UnityEngine;

/// <summary>
/// Toggles the Sucker on/off via RoverManager.
/// Uses RoverManager.Instance.Sucker instead of FindFirstObjectByType
/// since the Sucker is recreated each scene.
/// </summary>
public class ActivateVacuumButton : Interactable
{
    [Tooltip("The Sucker this button controls. If left empty, it will try to find one on this Rover.")]
    [SerializeField] private Sucker _targetSucker;

    private void Awake()
    {
        if (_targetSucker == null)
        {
            _targetSucker = GetComponentInParent<Sucker>();
        }
    }

    public override InteractionType OnInteract(GameObject user)
    {
        Debug.Log("ActivateVacuumButton Pressed");

        if (_targetSucker == null)
        {
            Debug.LogWarning("[ActivateVacuumButton] No Sucker assigned or found in parent!");
            return InteractionType.NONE;
        }

        _targetSucker.SetCanSuck(!_targetSucker.CanSuck());
        return InteractionType.PRESS;
    }
}