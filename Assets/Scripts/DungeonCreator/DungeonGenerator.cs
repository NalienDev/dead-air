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

    [Header("Door Placement")]
    [Tooltip("Moves the door up from the entry point. Set to half your door mesh height so it sits on the floor.")]
    [SerializeField] private float _doorYOffset = 1f;
    [Tooltip("Moves the door forward/back along the entry point's forward axis. Positive = into the room, negative = away.")]
    [SerializeField] private float _doorZOffset = 0f;
    [Tooltip("Extra Y rotation applied to the door at spawn. 0 or 180 depending on which way your mesh faces.")]
    [SerializeField] private float _doorYRotation = 0f;
    [Tooltip("Probability (0–1) that a door is spawned at each connection point. 1 = always, 0 = never.")]
    [Range(0f, 1f)]
    [SerializeField] private float _doorChance = 1f;

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

        if (!newGo.TryGetComponent(out DungeonPart newPart))
            return;

        if (!newPart.TryGetAvailableEntrypoint(out Transform newEntry))
        {
            Destroy(newGo);
            return;
        }

        _generatedRooms.Add(newPart);
        AlignRooms(newGo.transform, hostEntry, newEntry);

        // Spawn door AFTER alignment so it sits at the correctly snapped position
        GameObject doorGo = SpawnDoor(hostEntry);

        if (HasIntersection(newPart, hostRoom))
        {
            newPart.ReleaseEntrypoint(newEntry);
            hostRoom.ReleaseEntrypoint(hostEntry);
            _generatedRooms.Remove(newPart);
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
            if (!TryFindAvailableRoom(out DungeonPart hostRoom, out Transform hostEntry))
                continue;

            GameObject go = Instantiate(prefab, transform.position, transform.rotation);
            go.transform.SetParent(null);

            if (!go.TryGetComponent(out DungeonPart part)) continue;
            if (!part.TryGetAvailableEntrypoint(out Transform newEntry))
            {
                Destroy(go);
                continue;
            }

            _generatedRooms.Add(part);
            AlignRooms(go.transform, hostEntry, newEntry);

            // Spawn door AFTER alignment
            GameObject doorGo = SpawnDoor(hostEntry);

            if (HasIntersection(part, hostRoom))
            {
                part.ReleaseEntrypoint(newEntry);
                hostRoom.ReleaseEntrypoint(hostEntry);
                _generatedRooms.Remove(part);
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
            if (doorGo != null) Destroy(doorGo);   // ← null guard
            return;
        }

        if (!TryFindAvailableRoom(out DungeonPart hostRoom, out Transform hostEntry)) return;
        if (!partGo.TryGetComponent(out DungeonPart part)) return;
        if (!part.TryGetAvailableEntrypoint(out Transform newEntry)) return;

        AlignRooms(partGo.transform, hostEntry, newEntry);

        // Only reposition the door if one was actually spawned
        if (doorGo != null)
        {
            doorGo.transform.position = hostEntry.position
                                        + Vector3.up * _doorYOffset
                                        + hostEntry.forward * _doorZOffset;
            doorGo.transform.rotation = hostEntry.rotation * Quaternion.Euler(0f, _doorYRotation, 0f);
        }

        if (HasIntersection(part, hostRoom))
        {
            part.ReleaseEntrypoint(newEntry);
            hostRoom.ReleaseEntrypoint(hostEntry);
            RetryAttachment(partGo, doorGo, depth + 1);
        }
        else
        {
            _generatedRooms.Add(part);
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

    /// <summary>
    /// Returns true if <paramref name="part"/> overlaps any other placed DungeonPart.
    ///
    /// Only hits whose root GameObject has a <see cref="DungeonPart"/> component are counted.
    /// This means child objects (floors, walls, furniture, shelves) are ignored regardless of
    /// their layer — only room boundary colliders participate in the check.
    ///
    /// The host room we just connected to is excluded because touching at the connection
    /// point is expected and valid.
    /// </summary>
    private bool HasIntersection(DungeonPart part, DungeonPart hostRoom = null)
    {
        Physics.SyncTransforms();

        Collider col = part.collider;

        Collider[] hits = Physics.OverlapBox(
            col.bounds.center,
            col.bounds.size / 2f,
            col.transform.rotation,
            _roomsLayerMask
        );

        foreach (Collider hit in hits)
        {
            if (hit == col) continue;                                      // ignore self

            if (!hit.TryGetComponent(out DungeonPart hitPart)) continue;  // ignore child objects / furniture

            if (hostRoom != null && hitPart == hostRoom) continue;        // ignore the room we connected to

            return true;
        }

        return false;
    }

    private static void AlignRooms(Transform newRoom, Transform hostEntry, Transform newEntry)
    {
        // Rotate the new room so its entry forward faces OPPOSITE to the host entry forward
        // (they should face each other). Rotate around the new entry point as pivot,
        // not the room origin, so the entry point stays in place during rotation.
        float angleDiff = Vector3.SignedAngle(
            newEntry.forward, -hostEntry.forward, Vector3.up);

        newRoom.RotateAround(newEntry.position, Vector3.up, angleDiff);

        // After rotation, translate so the two entry points coincide
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

    // Spawns a door at an entry point, respecting _doorChance. Returns null if skipped.
    private GameObject SpawnDoor(Transform atEntry)
    {
        if (UnityEngine.Random.value > _doorChance)
            return null;

        Vector3 pos = atEntry.position
                      + Vector3.up * _doorYOffset
                      + atEntry.forward * _doorZOffset;
        Quaternion rot = atEntry.rotation * Quaternion.Euler(0f, _doorYRotation, 0f);
        return Instantiate(_door, pos, rot);
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