using PurrNet;
using UnityEngine;

/// <summary>
/// Base for networked objects the player can interact with.
/// </summary>
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