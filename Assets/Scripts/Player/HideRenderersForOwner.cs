using PurrNet;
using UnityEngine;

/// <summary>
/// Marks the third-person body — the model other players see — and keeps it
/// ACTIVE for everyone (so its NetworkAnimator keeps ticking and replicating),
/// while hiding its renderers on the owning client so the local player sees their
/// first-person models instead.
///
/// Use this INSTEAD of adding the object to NetworkOwnershipToggle's deactivate
/// list. Deactivating the GameObject stops its Animator/NetworkAnimator from
/// running on the owner, which prevents PurrNet from syncing its parameters.
///
/// Renderers are only hidden, not disabled on the Animator, so the pose still
/// evaluates and syncs. Swap enabled=false for shadowCastingMode = ShadowsOnly
/// if you want the local player to still cast a self-shadow.
/// </summary>
public class HideRenderersForOwner : NetworkBehaviour
{
    [Tooltip("If empty, all child renderers are collected automatically on spawn.")]
    [SerializeField] private Renderer[] _renderers;

    protected override void OnSpawned(bool asServer)
    {
        if (!isOwner) return;

        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);

        foreach (Renderer r in _renderers)
            if (r != null) r.enabled = false;
    }
}
