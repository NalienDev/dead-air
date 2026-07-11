using UnityEngine;

/// <summary>
/// The boarding area for going home: every alive player must stand inside it before the
/// <see cref="ReturnToBaseButton"/> works. Put it on a trigger collider around the rover
/// (child of the rover prefab works fine).
///
/// The button checks it client-side for instant feedback and the server re-validates in
/// <see cref="RoverManager.ServerRequestReturnToBase"/> — positions are replicated, so
/// both see the same answer.
/// </summary>
public class ReturnGatherZone : PlayerGatherZone
{
    public static ReturnGatherZone Instance { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// True if this player stands inside the return zone. The zone doubles as a safe
    /// area: the Echo and the Conductor drop targets that are inside it. False when no
    /// zone exists in the scene.
    /// </summary>
    public static bool IsInside(PlayerManager player) =>
        Instance != null && player != null && Instance.Contains(player.transform.position);
}
