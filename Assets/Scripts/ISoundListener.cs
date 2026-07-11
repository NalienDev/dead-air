using UnityEngine;

/// <summary>
/// Implemented by anything that reacts to sounds it hears in the world.
/// </summary>
public interface ISoundListener
{
    void OnHearSound(Vector3 origin);
}
