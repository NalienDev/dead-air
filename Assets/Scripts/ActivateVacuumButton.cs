using UnityEngine;

public class ActivateVacuumButton : Interactable
{
    public override InteractionType OnInteract(GameObject user)
    {
        FindFirstObjectByType<Sucker>().canSuck = !FindFirstObjectByType<Sucker>().canSuck;
        return InteractionType.PRESS;
    }
}
