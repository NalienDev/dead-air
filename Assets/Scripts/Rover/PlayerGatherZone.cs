using UnityEngine;

/// <summary>
/// Base for "everyone gather here" areas. Put it on an object with a trigger Collider
/// (any convex shape — box, sphere, capsule); the collider IS the area.
///
/// Positions are checked directly (not via OnTriggerEnter) so teleports, respawns and
/// CharacterControllers can never desync the inside/outside state, and the same check
/// works on both server and clients (player positions are replicated).
/// Dead players are ignored — they're spectating ghosts.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PlayerGatherZone : MonoBehaviour
{
    private Collider _collider;

    protected virtual void Awake()
    {
        _collider = GetComponent<Collider>();
        if (!_collider.isTrigger)
        {
            Debug.LogWarning($"[{GetType().Name}] Collider should be a trigger. Forcing isTrigger = true.", this);
            _collider.isTrigger = true;
        }
    }

    /// <summary>True if a world position is inside this zone's collider.</summary>
    public bool Contains(Vector3 worldPos)
    {
        // A point inside a convex collider is its own closest point.
        return (_collider.ClosestPoint(worldPos) - worldPos).sqrMagnitude <= 0.0025f;
    }

    /// <summary>
    /// True when every ALIVE player is inside the zone. <paramref name="aliveCount"/>
    /// reports how many alive players exist (0 means "nobody to gather" — treat as false).
    /// </summary>
    public bool AreAllAlivePlayersInside(out int aliveCount)
    {
        aliveCount = 0;

        foreach (PlayerManager pm in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
        {
            if (pm.IsDead) continue;
            aliveCount++;
            if (!Contains(pm.transform.position)) return false;
        }

        return aliveCount > 0;
    }

    public bool AreAllAlivePlayersInside() => AreAllAlivePlayersInside(out _);

    /// <summary>True if any player is currently flagged as inside the dungeon.</summary>
    protected static bool AnyPlayerInsideDungeon()
    {
        foreach (PlayerManager pm in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
            if (pm.IsInsideDungeon()) return true;
        return false;
    }
}
