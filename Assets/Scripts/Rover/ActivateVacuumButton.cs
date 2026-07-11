using UnityEngine;

/// <summary>
/// Interactable button that activates its rover's Sucker.
/// </summary>
public class ActivateVacuumButton : Interactable
{
    [Tooltip("The Sucker this button controls. Auto-found on the Rover if empty.")]
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

        if (_targetSucker.CanSuck())
        {
            return InteractionType.NONE;
        }

        _targetSucker.ActivateVacuum();
        return InteractionType.PRESS;
    }
}