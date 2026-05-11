using PurrNet;
using UnityEngine;

public class BandwidthObject : GrabbableObject
{
    [SerializeField] private int _bandwidthValue = 100;

    public int BandwidthValue => _bandwidthValue;

    public override InteractionType OnInteract(GameObject user)
    {
        // We still want to be able to pick it up
        return base.OnInteract(user);
    }
}
