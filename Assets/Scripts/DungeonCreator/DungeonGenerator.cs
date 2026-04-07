using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative dungeon generator.
/// Runs exclusively on the server; all Instantiate() calls are
/// automatically propagated to clients by PurrNet.
/// </summary>
public class DungeonGenerator : NetworkBehaviour
{
    public static DungeonGenerator Instance { get; private set; }

    /// <summary>Raised on the server (and host-client) once generation completes.</summary>
    public event Action OnGenerated;

    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Dungeon Parts")]
    [SerializeField] private GameObject _entrance;
    [SerializeField] private List<GameObject> _rooms;
    [SerializeField] private List<GameObject> _specialRooms;
    [SerializeField] private List<GameObject> _alternateEntrances;
    [SerializeField] private List<GameObject> _hallways;
    [SerializeField] private GameObject _door;

    [Header("Generation Settings")]
    [SerializeField] private int _targetRoomCount = 10;
    [SerializeField] private float _generationTickInterval = 0.1f;
    [SerializeField] private float _hallwayChance = 0.5f;
    [SerializeField] private float _specialRoomChance = 0.1f;
    [SerializeField] private LayerMask _roomsLayerMask;

    // ── State ──────────────────────────────────────────────────────────────

    private readonly List<DungeonPart> _generatedRooms = new();
    private bool _isGenerated = false;
    private bool _shouldGenerate = false;
    private bool _generationPaused = false;
    private float _tickTimer = 0f;

    private const int MaxRetries = 100;

    // ── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        Debug.Log("is server?" + isServer + "is generated? " + _isGenerated + "should not generate ? " + !_shouldGenerate);
        if (!isServer || _isGenerated || !_shouldGenerate) return;

        if (_generationPaused)
        {
            _tickTimer += Time.deltaTime;
            if (_tickTimer >= _generationTickInterval)
            {
                _generationPaused = false;
                _tickTimer = 0f;
            }
            return;
        }

        int normalRoomTarget = _targetRoomCount - _alternateEntrances.Count;
        if (_generatedRooms.Count >= normalRoomTarget)
        {
            FinalizeGeneration();
            return;
        }

        GenerateNextPart();
        _generationPaused = true;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Kicks off dungeon generation. Server-only.</summary>
    public void StartGeneration()
    {
        if (!isServer || _isGenerated) return;
        _shouldGenerate = true;
    }

    public List<DungeonPart> GetGeneratedRooms() => _generatedRooms;
    public bool IsGenerated() => _isGenerated;

    // ── Core generation ────────────────────────────────────────────────────

    private void GenerateNextPart()
    {
        if (_generatedRooms.Count == 0)
        {
            SpawnEntrance();
            return;
        }

        bool placeHallway = UnityEngine.Random.value < _hallwayChance && _hallways.Count > 0;
        GameObject prefab = placeHallway ? PickRandom(_hallways) : PickRoomPrefab();
        TryAttachPart(prefab);
    }

    private void SpawnEntrance()
    {
        // PurrNet: plain Instantiate() auto-spawns across the network
        GameObject go = Instantiate(_entrance, transform.position, transform.rotation);
        go.transform.SetParent(null);

        if (go.TryGetComponent(out DungeonPart part))
            _generatedRooms.Add(part);
    }

    private void TryAttachPart(GameObject prefab)
    {
        if (!TryFindAvailableRoom(out DungeonPart hostRoom, out Transform hostEntry))
            return;

        GameObject newGo = Instantiate(prefab, transform.position, transform.rotation);
        newGo.transform.SetParent(null);

        GameObject doorGo = Instantiate(_door, hostEntry.position, hostEntry.rotation);

        if (!newGo.TryGetComponent(out DungeonPart newPart))
            return;

        if (!newPart.TryGetAvailableEntrypoint(out Transform newEntry))
        {
            Destroy(newGo);
            Destroy(doorGo);
            return;
        }

        _generatedRooms.Add(newPart);
        AlignRooms(hostRoom.transform, newGo.transform, hostEntry, newEntry);

        if (HasIntersection(newPart))
        {
            newPart.ReleaseEntrypoint(newEntry);
            hostRoom.ReleaseEntrypoint(hostEntry);
            RetryAttachment(newGo, doorGo);
        }
    }

    private void FinalizeGeneration()
    {
        SpawnAlternateEntrances();

        foreach (DungeonPart room in _generatedRooms)
            room.FillEmptyDoors();

        _isGenerated = true;
        OnGenerated?.Invoke();
    }

    // ── Alternate entrances ────────────────────────────────────────────────

    private void SpawnAlternateEntrances()
    {
        if (_alternateEntrances.Count == 0) return;

        foreach (GameObject prefab in _alternateEntrances)
        {
            if (!TryFindAvitableRoom(out DungeonPart hostRoom, out Transform hostEntry))
                continue;

            GameObject go = Instantiate(prefab, transform.position, transform.rotation);
            go.transform.SetParent(null);

            GameObject doorGo = Instantiate(_door, hostEntry.position, hostEntry.rotation);

            if (!go.TryGetComponent(out DungeonPart part)) continue;
            if (!part.TryGetAvailableEntrypoint(out Transform newEntry))
            {
                Destroy(go);
                Destroy(doorGo);
                continue;
            }

            _generatedRooms.Add(part);
            AlignRooms(hostRoom.transform, go.transform, hostEntry, newEntry);

            if (HasIntersection(part))
            {
                part.ReleaseEntrypoint(newEntry);
                hostRoom.ReleaseEntrypoint(hostEntry);
                RetryAttachment(go, doorGo);
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void RetryAttachment(GameObject partGo, GameObject doorGo, int depth = 0)
    {
        if (depth >= MaxRetries)
        {
            Debug.LogWarning("[DungeonGenerator] Max retry depth reached. Skipping part.");
            Destroy(partGo);
            Destroy(doorGo);
            return;
        }

        if (!TryFindAvailableRoom(out DungeonPart hostRoom, out Transform hostEntry)) return;
        if (!partGo.TryGetComponent(out DungeonPart part)) return;
        if (!part.TryGetAvailableEntrypoint(out Transform newEntry)) return;

        doorGo.transform.SetPositionAndRotation(hostEntry.position, hostEntry.rotation);
        AlignRooms(hostRoom.transform, partGo.transform, hostEntry, newEntry);

        if (HasIntersection(part))
        {
            part.ReleaseEntrypoint(newEntry);
            hostRoom.ReleaseEntrypoint(hostEntry);
            RetryAttachment(partGo, doorGo, depth + 1);
        }
    }

    private bool TryFindAvailableRoom(out DungeonPart foundRoom, out Transform foundEntry)
    {
        foundRoom = null;
        foundEntry = null;

        List<DungeonPart> shuffled = new List<DungeonPart>(_generatedRooms);
        ShuffleList(shuffled);

        foreach (DungeonPart candidate in shuffled)
        {
            if (candidate.TryGetAvailableEntrypoint(out Transform entry))
            {
                foundRoom = candidate;
                foundEntry = entry;
                return true;
            }
        }
        return false;
    }

    // Alias so both SpawnAlternateEntrances and the rest of the code compile cleanly
    private bool TryFindAvitableRoom(out DungeonPart foundRoom, out Transform foundEntry)
        => TryFindAvailableRoom(out foundRoom, out foundEntry);

    private bool HasIntersection(DungeonPart part)
    {
        Collider[] hits = Physics.OverlapBox(
            part.collider.bounds.center,
            part.collider.bounds.size / 2f,
            Quaternion.identity,
            _roomsLayerMask
        );

        foreach (Collider hit in hits)
        {
            if (hit != part.collider)
                return true;
        }
        return false;
    }

    private static void AlignRooms(
        Transform hostRoom, Transform newRoom,
        Transform hostEntry, Transform newEntry)
    {
        float angleDiff = Vector3.SignedAngle(
            newEntry.forward, -hostEntry.forward, Vector3.up);
        newRoom.Rotate(Vector3.up, angleDiff, Space.World);

        Vector3 offset = hostEntry.position - newEntry.position;
        newRoom.position += offset;

        Physics.SyncTransforms();
    }

    private GameObject PickRoomPrefab()
    {
        bool canPlaceSpecial = _specialRooms.Count > 0
                               && UnityEngine.Random.value < _specialRoomChance;
        return canPlaceSpecial ? PickRandom(_specialRooms) : PickRandom(_rooms);
    }

    private static GameObject PickRandom(List<GameObject> list)
        => list[UnityEngine.Random.Range(0, list.Count)];

    private static void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
