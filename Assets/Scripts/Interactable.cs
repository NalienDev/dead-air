using PurrNet;
using UnityEngine;

public abstract class Interactable : NetworkIdentity
{
    public abstract InteractionType OnInteract(GameObject user);
}

public enum InteractionType
{
    NONE,
    PRESS,
    GRAB,
}