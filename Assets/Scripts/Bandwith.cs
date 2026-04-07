using PurrNet;
using UnityEngine;

public class Bandwith : NetworkIdentity
{
    public SyncVar<int> currentBandwith = new SyncVar<int>(2000);
}
