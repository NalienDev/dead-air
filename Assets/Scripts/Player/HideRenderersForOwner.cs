using PurrNet;
using UnityEngine;

/// <summary>
/// Hides the third-person body's renderers for the owner while keeping its animator ticking and replicating.
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
