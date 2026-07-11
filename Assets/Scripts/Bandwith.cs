using PurrNet;
using UnityEngine;

/// <summary>
/// Network-synced bandwidth counter.
/// </summary>
public class Bandwith : NetworkIdentity
{
    public SyncVar<int> currentBandwith = new SyncVar<int>(2000);
}
