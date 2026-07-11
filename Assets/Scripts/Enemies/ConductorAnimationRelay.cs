using UnityEngine;

/// <summary>
/// Relays animation events from the Conductor's model root up to TheConductorAI on the parent.
/// </summary>
public class ConductorAnimationRelay : MonoBehaviour
{
    private TheConductorAI _conductor;

    private void Awake()
    {
        _conductor = GetComponentInParent<TheConductorAI>();
        if (_conductor == null)
            Debug.LogError("[ConductorAnimationRelay] No TheConductorAI found on a parent.", this);
    }

    // Animation event fired on the attack clip's hit frame.
    public void OnAttackHit()
    {
        if (_conductor != null) _conductor.OnAttackHit();
    }

    // Animation event fired on the shout clip.
    public void OnShout()
    {
        if (_conductor != null) _conductor.OnShout();
    }
}
