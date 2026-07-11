using UnityEngine;

/// <summary>
/// Base for gather areas that test whether alive players stand inside a trigger collider.
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

    public bool Contains(Vector3 worldPos)
    {
        // A point inside a convex collider is its own closest point.
        return (_collider.ClosestPoint(worldPos) - worldPos).sqrMagnitude <= 0.0025f;
    }

    // True when every alive player is inside the zone; aliveCount reports how many exist.
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

    protected static bool AnyPlayerInsideDungeon()
    {
        foreach (PlayerManager pm in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
            if (pm.IsInsideDungeon()) return true;
        return false;
    }
}
