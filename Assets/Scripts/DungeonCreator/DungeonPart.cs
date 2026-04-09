using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Represents a single networked piece of the dungeon (room or hallway).
/// Responsible for tracking its own entry points and spawning filler walls.
/// </summary>
public class DungeonPart : NetworkBehaviour
{
    public enum DungeonPartType { Room, Hallway }

    // ── Inspector ──────────────────────────────────────────────────────────

    [SerializeField] private DungeonPartType _dungeonPartType;
    [SerializeField] private GameObject _fillerWall;
    [SerializeField] private Transform _monsterSpawnLocation;
    [SerializeField] private LayerMask _roomsLayerMask;

    [Header("Filler Wall Placement")]
    [Tooltip("Moves the filler wall up from the entry point. Set to half your wall mesh height.")]
    [SerializeField] private float _fillerWallYOffset = 1f;
    [Tooltip("Extra Y rotation applied to the filler wall. Use 180 if the wall faces the wrong way.")]
    [SerializeField] private float _fillerWallYRotation = 180f;

    public List<Transform> entrypoints;
    public new Collider collider;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns an unoccupied entry point chosen at random, marking it occupied.
    /// Returns false if none are available.
    /// </summary>
    public bool TryGetAvailableEntrypoint(out Transform entrypoint)
    {
        entrypoint = null;

        if (entrypoints == null || entrypoints.Count == 0)
            return false;

        if (entrypoints.Count == 1)
            return TryOccupyEntry(entrypoints[0], out entrypoint);

        // Shuffle to avoid retry-hammering the same entries
        List<Transform> shuffled = new List<Transform>(entrypoints);
        ShuffleList(shuffled);

        foreach (Transform entry in shuffled)
        {
            if (TryOccupyEntry(entry, out entrypoint))
                return true;
        }

        return false;
    }

    /// <summary>Marks a previously occupied entry point as free again.</summary>
    public void ReleaseEntrypoint(Transform entrypoint)
    {
        if (entrypoint != null && entrypoint.TryGetComponent(out EntryPoint ep))
            ep.SetOccupied(false);
    }

    public Transform GetMonsterSpawnLocation() => _monsterSpawnLocation;

    /// <summary>
    /// Spawns a filler wall on every unoccupied entry point.
    /// Must be called on the server — PurrNet auto-syncs the Instantiate to clients.
    /// </summary>
    public void FillEmptyDoors()
    {
        foreach (Transform entry in entrypoints)
        {
            if (!entry.TryGetComponent(out EntryPoint ep) || ep.IsOccupied())
                continue;

            Vector3 pos = entry.position + Vector3.up * _fillerWallYOffset;
            Quaternion rot = entry.rotation * Quaternion.Euler(0f, _fillerWallYRotation, 0f);
            GameObject wall = Instantiate(_fillerWall, pos, rot);
            wall.transform.SetParent(null);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private static bool TryOccupyEntry(Transform entry, out Transform result)
    {
        result = null;
        if (!entry.TryGetComponent(out EntryPoint ep) || ep.IsOccupied())
            return false;

        ep.SetOccupied(true);
        result = entry;
        return true;
    }

    private static void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ── Editor gizmos ──────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (collider == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
    }
}