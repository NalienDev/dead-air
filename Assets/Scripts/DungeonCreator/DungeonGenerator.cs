using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative dungeon generator.
/// Runs exclusively on the server; all Instantiate() calls are
/// automatically propagated to clients by PurrNet.
///
/// If placement fails too many times in a single generation attempt,
/// the generator tears everything down and restarts automatically.
/// </summary>
/// <summary>Outcome of a single part placement attempt.</summary>
public enum PlacementResult
{
    Success,
    NoSlotAvailable,  // No free entry point exists yet — not a geometry problem
    GeometryFailure,  // A slot was found but every position intersected another room
}

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
    [Tooltip("Probability (0-1) that a door is spawned at each connection point. 1 = always, 0 = never.")]
    [Range(0f, 1f)]
    [SerializeField] private float _doorChance = 1f;

    [Header("Generation Settings")]
    [SerializeField] private int _targetRoomCount = 10;
    [SerializeField] private float _generationTickInterval = 0.1f;
    [SerializeField] private float _hallwayChance = 0.5f;
    [SerializeField] private float _specialRoomChance = 0.1f;
    [SerializeField] private LayerMask _roomsLayerMask;

    [Header("Failure Recovery")]
    [Tooltip("How many consecutive placement failures before the entire dungeon is scrapped and restarted.")]
    [SerializeField] private int _maxConsecutiveFailures = 10;
    [Tooltip("How many full restart attempts before giving up entirely and logging an error.")]
    [SerializeField] private int _maxRestartAttempts = 20;

    // ── State ──────────────────────────────────────────────────────────────

    private readonly List<DungeonPart> _generatedRooms = new();
    private readonly List<GameObject> _spawnedDoors = new();
    private bool _isGenerated = false;
    private bool _shouldGenerate = false;
    private bool _generationPaused = false;
    private float _tickTimer = 0f;

    private int _consecutiveFailures = 0;
    private int _restartAttempts = 0;

    private const int MaxPlacementRetries = 100;

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

        PlacementResult result = TryAttachPart(prefab);

        switch (result)
        {
            case PlacementResult.Success:
                _consecutiveFailures = 0;
                break;

            case PlacementResult.GeometryFailure:
                _consecutiveFailures++;
                Debug.LogWarning($"[DungeonGenerator] Placement failure #{_consecutiveFailures}/{_maxConsecutiveFailures}.");
                if (_consecutiveFailures >= _maxConsecutiveFailures)
                    RestartGeneration();
                break;

            case PlacementResult.NoSlotAvailable:
                // All current entry points are occupied — not a geometry problem,
                // just wait for the next tick; do not touch the failure counter.
                break;
        }
    }

    private void SpawnEntrance()
    {
        GameObject go = Instantiate(_entrance, transform.position, transform.rotation);
        go.transform.SetParent(null);

        if (go.TryGetComponent(out DungeonPart part))
            _generatedRooms.Add(part);
    }

    /// <summary>
    /// Instantiates <paramref name="prefab"/>, finds a valid non-overlapping position for it,
    /// and adds it to <see cref="_generatedRooms"/>.
    /// The part is only ever added to the list once placement is confirmed successful —
    /// never speculatively, so destroyed objects can never linger in the list.
    /// Returns true on success, false if the part had to be abandoned.
    /// </summary>
    private PlacementResult TryAttachPart(GameObject prefab)
    {
        if (!TryFindAvailableRoom(out DungeonPart hostRoom, out Transform hostEntry))
            return PlacementResult.NoSlotAvailable;

        GameObject newGo = Instantiate(prefab, transform.position, transform.rotation);
        newGo.transform.SetParent(null);

        if (!newGo.TryGetComponent(out DungeonPart newPart))
        {
            Destroy(newGo);
            return PlacementResult.GeometryFailure;
        }

        if (!newPart.TryGetAvailableEntrypoint(out Transform newEntry))
        {
            Destroy(newGo);
            return PlacementResult.GeometryFailure;
        }

        AlignRooms(newGo.transform, hostEntry, newEntry);
        GameObject doorGo = SpawnDoor(hostEntry);

        // No intersection on first try — commit immediately
        if (!HasIntersection(newPart, hostRoom))
        {
            _generatedRooms.Add(newPart);
            return PlacementResult.Success;
        }

        // First position intersected — release and try other entry point combos
        newPart.ReleaseEntrypoint(newEntry);
        hostRoom.ReleaseEntrypoint(hostEntry);

        bool success = RetryAttachment(newGo, doorGo);

        if (!success)
        {
            Destroy(newGo);
            if (doorGo != null) Destroy(doorGo);
            return PlacementResult.GeometryFailure;
        }

        return PlacementResult.Success;
    }

    private void FinalizeGeneration()
    {
        SpawnAlternateEntrances();

        foreach (DungeonPart room in _generatedRooms)
            room.FillEmptyDoors();

        _isGenerated = true;
        Debug.Log($"[DungeonGenerator] Generation complete after {_restartAttempts} restart(s). {_generatedRooms.Count} parts placed.");
        OnGenerated?.Invoke();
    }

    // ── Full restart ───────────────────────────────────────────────────────

    private void RestartGeneration()
    {
        _restartAttempts++;

        if (_restartAttempts > _maxRestartAttempts)
        {
            Debug.LogError($"[DungeonGenerator] Failed to generate dungeon after {_maxRestartAttempts} restart attempts. " +
                           "Consider increasing room variety, reducing target room count, or checking prefab colliders.");
            _shouldGenerate = false;
            return;
        }

        Debug.LogWarning($"[DungeonGenerator] Restarting generation (attempt {_restartAttempts}/{_maxRestartAttempts}).");

        foreach (DungeonPart part in _generatedRooms)
        {
            if (part != null)
                Destroy(part.gameObject);
        }

        foreach (GameObject door in _spawnedDoors)
        {
            if (door != null)
                Destroy(door);
        }

        _generatedRooms.Clear();
        _spawnedDoors.Clear();
        _consecutiveFailures = 0;
        _generationPaused = false;
        _tickTimer = 0f;
        // _shouldGenerate stays true so Update() resumes automatically next tick
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

            AlignRooms(go.transform, hostEntry, newEntry);
            GameObject doorGo = SpawnDoor(hostEntry);

            // Only add to the list once we know it doesn't intersect
            if (!HasIntersection(part, hostRoom))
            {
                _generatedRooms.Add(part);
                continue;
            }

            // Intersected — release and retry without touching _generatedRooms
            part.ReleaseEntrypoint(newEntry);
            hostRoom.ReleaseEntrypoint(hostEntry);

            bool placed = RetryAttachment(go, doorGo);
            if (!placed)
            {
                Destroy(go);
                if (doorGo != null) Destroy(doorGo);
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Tries all remaining available entry point combinations to place <paramref name="partGo"/>.
    /// On success, adds the part to <see cref="_generatedRooms"/> and returns true.
    /// On failure, returns false — the caller is responsible for destroying the GameObjects.
    /// </summary>
    private bool RetryAttachment(GameObject partGo, GameObject doorGo, int depth = 0)
    {
        if (depth >= MaxPlacementRetries)
        {
            Debug.LogWarning("[DungeonGenerator] Max per-part retry depth reached — abandoning this part.");
            return false;
        }

        if (!TryFindAvailableRoom(out DungeonPart hostRoom, out Transform hostEntry)) return false;
        if (!partGo.TryGetComponent(out DungeonPart part)) return false;
        if (!part.TryGetAvailableEntrypoint(out Transform newEntry)) return false;

        AlignRooms(partGo.transform, hostEntry, newEntry);

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
            // BUG WAS HERE: old code did `RetryAttachment(...); return false`
            // which discarded the recursive success result and always told the
            // caller the part failed, causing it to Destroy() a part that was
            // actually placed — leaving dead Transform references in _generatedRooms.
            return RetryAttachment(partGo, doorGo, depth + 1);
        }

        // Valid position found — commit
        _generatedRooms.Add(part);
        return true;
    }

    /// <summary>
    /// Finds any placed room that still has a free entry point.
    /// The list is shuffled first to avoid always connecting to the same rooms.
    /// Skips any rooms whose GameObject has been destroyed (safety guard during restarts).
    /// </summary>
    private bool TryFindAvailableRoom(out DungeonPart foundRoom, out Transform foundEntry)
    {
        foundRoom = null;
        foundEntry = null;

        List<DungeonPart> shuffled = new List<DungeonPart>(_generatedRooms);
        ShuffleList(shuffled);

        foreach (DungeonPart candidate in shuffled)
        {
            // Guard against destroyed parts that may still be in the list mid-restart
            if (candidate == null) continue;

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
    /// Only root GameObjects with a <see cref="DungeonPart"/> component are counted.
    /// Child objects (floors, walls, furniture) are ignored regardless of layer.
    /// The host room is excluded because touching at the connection point is expected.
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
            if (hit == col) continue;
            if (!hit.TryGetComponent(out DungeonPart hitPart)) continue;
            if (hostRoom != null && hitPart == hostRoom) continue;
            return true;
        }

        return false;
    }

    private static void AlignRooms(Transform newRoom, Transform hostEntry, Transform newEntry)
    {
        float angleDiff = Vector3.SignedAngle(
            newEntry.forward, -hostEntry.forward, Vector3.up);

        newRoom.RotateAround(newEntry.position, Vector3.up, angleDiff);

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

    private GameObject SpawnDoor(Transform atEntry)
    {
        if (UnityEngine.Random.value > _doorChance)
            return null;

        Vector3 pos = atEntry.position
                      + Vector3.up * _doorYOffset
                      + atEntry.forward * _doorZOffset;
        Quaternion rot = atEntry.rotation * Quaternion.Euler(0f, _doorYRotation, 0f);
        GameObject door = Instantiate(_door, pos, rot);
        _spawnedDoors.Add(door);
        return door;
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