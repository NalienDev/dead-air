using UnityEngine;

public class ActivateVacuumButton : Interactable
{
    public override InteractionType OnInteract(GameObject user)
    {
        FindFirstObjectByType<Sucker>().SetCanSuck(!FindFirstObjectByType<Sucker>().CanSuck());
        return InteractionType.PRESS;
    }
}
