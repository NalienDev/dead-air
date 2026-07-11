using UnityEngine;

/// <summary>
/// Boarding area every alive player must stand in to return home, which also counts as a safe zone.
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

    // True if the player stands inside the return zone, which enemies treat as a safe area.
    public static bool IsInside(PlayerManager player) =>
        Instance != null && player != null && Instance.Contains(player.transform.position);
}
