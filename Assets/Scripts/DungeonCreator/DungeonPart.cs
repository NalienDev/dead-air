using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// A single networked dungeon piece that tracks its entry points and spawns filler walls.
/// </summary>
public class DungeonPart : NetworkBehaviour
{
    public enum DungeonPartType { Room, Hallway }

    [SerializeField] private DungeonPartType _dungeonPartType;
    [SerializeField] private GameObject _fillerWall;
    [SerializeField] private Transform _monsterSpawnLocation;
    [SerializeField] private LayerMask _roomsLayerMask;

    [Header("Filler Wall Placement")]
    [Tooltip("Vertical offset of the filler wall from the entry point.")]
    [SerializeField] private float _fillerWallYOffset = 1f;
    [Tooltip("Extra Y rotation applied to the filler wall.")]
    [SerializeField] private float _fillerWallYRotation = 180f;

    public List<Transform> entrypoints;
    public new Collider collider;

    // Returns a random unoccupied entry point and marks it occupied, or false if none are free.
    public bool TryGetAvailableEntrypoint(out Transform entrypoint)
    {
        entrypoint = null;

        if (entrypoints == null || entrypoints.Count == 0)
            return false;

        if (entrypoints.Count == 1)
            return TryOccupyEntry(entrypoints[0], out entrypoint);

        // Shuffle to avoid retry-hammering the same entries.
        List<Transform> shuffled = new List<Transform>(entrypoints);
        ShuffleList(shuffled);

        foreach (Transform entry in shuffled)
        {
            if (TryOccupyEntry(entry, out entrypoint))
                return true;
        }

        return false;
    }

    public void ReleaseEntrypoint(Transform entrypoint)
    {
        if (entrypoint != null && entrypoint.TryGetComponent(out EntryPoint ep))
            ep.SetOccupied(false);
    }

    public Transform GetMonsterSpawnLocation() => _monsterSpawnLocation;

    // Spawns a filler wall on every unoccupied entry point; call on the server.
    public void FillEmptyDoors(System.Collections.Generic.List<GameObject> wallRegistry = null)
    {
        foreach (Transform entry in entrypoints)
        {
            if (!entry.TryGetComponent(out EntryPoint ep) || ep.IsOccupied())
                continue;

            Vector3 pos = entry.position + Vector3.up * _fillerWallYOffset;
            Quaternion rot = entry.rotation * Quaternion.Euler(0f, _fillerWallYRotation, 0f);
            GameObject wall = Instantiate(_fillerWall, pos, rot);
            wall.transform.SetParent(null);

            wallRegistry?.Add(wall);
        }
    }

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

    private void OnDrawGizmos()
    {
        if (collider == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
    }
}