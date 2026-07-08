using UnityEngine;

/// <summary>
/// Forwards the attack animation event up to <see cref="TheConductorAI"/>.
///
/// Unity animation events only reach components on the SAME GameObject as the Animator.
/// The Conductor's Animator lives on the model root, but the AI script lives on the
/// network root above it — so the event can't call the AI directly. Put this component
/// on the model root (next to the Animator) and point the attack clip's animation event
/// at <see cref="OnAttackHit"/>; it relays the call to the AI on the parent.
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

    /// <summary>
    /// Animation event target. Add this to the attack clip on the "slicing" pose frame
    /// (Function: <c>OnAttackHit</c>, no parameters). Fires on every client; the AI
    /// decides who gets hurt.
    /// </summary>
    public void OnAttackHit()
    {
        if (_conductor != null) _conductor.OnAttackHit();
    }

    /// <summary>
    /// Animation event target for the Shout (post-attack) clip. Add it wherever you
    /// want the screech to sound (Function: <c>OnShout</c>, no parameters).
    /// </summary>
    public void OnShout()
    {
        if (_conductor != null) _conductor.OnShout();
    }
}
