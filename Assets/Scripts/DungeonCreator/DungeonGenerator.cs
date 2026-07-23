using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Outcome of a single part placement attempt.
/// </summary>
public enum PlacementResult
{
    Success,
    NoSlotAvailable,  // No free entry point exists yet, not a geometry problem
    GeometryFailure,  // A slot was found but every position intersected another room
}

/// <summary>
/// Server-authoritative generator that assembles the dungeon from part prefabs and restarts on repeated placement failures.
/// </summary>
public class DungeonGenerator : NetworkBehaviour
{
    public static DungeonGenerator Instance { get; private set; }

    // Raised on the server once generation completes.
    public event Action OnGenerated;

    [Header("Dungeon Parts")]
    [SerializeField] private GameObject _entrance;
    [SerializeField] private List<GameObject> _rooms;
    [SerializeField] private List<GameObject> _specialRooms;
    [SerializeField] private List<GameObject> _alternateEntrances;
    [SerializeField] private List<GameObject> _hallways;
    [SerializeField] private GameObject _door;

    [Header("Door Placement")]
    [Tooltip("Vertical offset of the door from the entry point.")]
    [SerializeField] private float _doorYOffset = 1f;
    [Tooltip("Offset along the entry point's forward axis. Positive = into the room.")]
    [SerializeField] private float _doorZOffset = 0f;
    [Tooltip("Extra Y rotation applied to the door at spawn.")]
    [SerializeField] private float _doorYRotation = 0f;
    [Tooltip("Chance a door spawns at each connection point.")]
    [Range(0f, 1f)]
    [SerializeField] private float _doorChance = 1f;

    [Header("Generation Settings")]
    [SerializeField] private int _targetRoomCount = 10;
    [SerializeField] private float _generationTickInterval = 0.1f;
    [SerializeField] private float _hallwayChance = 0.5f;
    [SerializeField] private float _specialRoomChance = 0.1f;
    [SerializeField] private LayerMask _roomsLayerMask;

    [Header("Bandwidth Loot Spawning")]
    [Tooltip("Loot prefabs (bandwidth objects) picked at random for each spawn.")]
    [SerializeField] private List<GameObject> _lootPrefabs = new();
    [Tooltip("Chance each Bandwidth LootSpawnPoint in a part actually spawns an object.")]
    [SerializeField, Range(0f, 1f)] private float _lootChancePerPoint = 0.5f;
    [Tooltip("Max objects spawned per part. 0 = no cap.")]
    [SerializeField, Min(0)] private int _maxLootPerRoom = 3;
    [Tooltip("Max objects across the whole dungeon. 0 = no cap.")]
    [SerializeField, Min(0)] private int _maxTotalLoot = 0;
    [Tooltip("Guaranteed minimum across the whole dungeon (fills random empty points if the rolls came up short).")]
    [SerializeField, Min(0)] private int _minTotalLoot = 0;
    [Tooltip("Give each spawned object a random Y rotation instead of the spawn point's.")]
    [SerializeField] private bool _randomizeLootYRotation = true;

    [Header("Energy Cell Spawning")]
    [Tooltip("Energy cell prefabs picked at random for each spawn.")]
    [SerializeField] private List<GameObject> _energyCellPrefabs = new();
    [Tooltip("Chance each EnergyCell LootSpawnPoint in a part actually spawns a cell.")]
    [SerializeField, Range(0f, 1f)] private float _cellChancePerPoint = 0.25f;
    [Tooltip("Max cells spawned per part. 0 = no cap.")]
    [SerializeField, Min(0)] private int _maxCellsPerRoom = 1;
    [Tooltip("Max cells across the whole dungeon. 0 = no cap.")]
    [SerializeField, Min(0)] private int _maxTotalCells = 0;
    [Tooltip("Guaranteed minimum of cells across the whole dungeon.")]
    [SerializeField, Min(0)] private int _minTotalCells = 0;

    [Header("Failure Recovery")]
    [Tooltip("Consecutive placement failures before the dungeon is scrapped and restarted.")]
    [SerializeField] private int _maxConsecutiveFailures = 10;
    [Tooltip("Full restart attempts before giving up and logging an error.")]
    [SerializeField] private int _maxRestartAttempts = 20;

    private readonly List<DungeonPart> _generatedRooms = new();
    private readonly List<GameObject> _spawnedDoors = new();
    private readonly List<GameObject> _spawnedFillerWalls = new();

    // True once generation is complete; synced to all clients.
    public SyncVar<bool> isGenerated = new(false);
    private bool _isGenerated => isGenerated.value;
    private bool _shouldGenerate = false;
    private bool _generationPaused = false;
    private float _tickTimer = 0f;

    private int _consecutiveFailures = 0;
    private int _restartAttempts = 0;

    private const int MaxPlacementRetries = 100;

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

    // Kicks off dungeon generation; server-only.
    public void StartGeneration()
    {
        if (!isServer || isGenerated.value) return;
        _shouldGenerate = true;
    }

    // Destroys the current dungeon and restarts generation cleanly; server-only.
    public void RegenerateDungeon()
    {
        if (!isServer) return;

        Debug.Log($"[DungeonGenerator] RegenerateDungeon called (frame {Time.frameCount}).");

        CleanupDungeonLoot(includeRoomChildren: true);

        foreach (DungeonPart part in _generatedRooms)
        {
            if (part != null) Destroy(part.gameObject);
        }

        foreach (GameObject door in _spawnedDoors)
        {
            if (door != null) Destroy(door);
        }

        foreach (GameObject wall in _spawnedFillerWalls)
        {
            if (wall != null) Destroy(wall);
        }

        _generatedRooms.Clear();
        _spawnedDoors.Clear();
        _spawnedFillerWalls.Clear();
        _consecutiveFailures = 0;
        _restartAttempts = 0;
        isGenerated.value = false;
        _generationPaused = false;
        _tickTimer = 0f;
        _shouldGenerate = true;
    }

    public List<DungeonPart> GetGeneratedRooms() => _generatedRooms;
    public bool IsGenerated() => isGenerated.value;

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
                // All entry points occupied; wait for the next tick without counting a failure.
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

    // Spawns the prefab and finds a non-overlapping position, adding it to the list only
    // once placement is confirmed so destroyed objects never linger there.
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

        // No intersection on first try: commit immediately.
        if (!HasIntersection(newPart, hostRoom))
        {
            _generatedRooms.Add(newPart);
            return PlacementResult.Success;
        }

        // First position intersected: release and try other entry point combos.
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
            room.FillEmptyDoors(_spawnedFillerWalls);

        // Failed placements may have orphaned loot at the generator origin; sweep it
        // before opening the dungeon.
        CleanupDungeonLoot(includeRoomChildren: false);

        isGenerated.value = true;

        // After isGenerated so freshly spawned loot skips the generation freeze entirely.
        SpawnRoomLoot();

        Debug.Log($"[DungeonGenerator] Generation complete after {_restartAttempts} restart(s). {_generatedRooms.Count} parts placed.");
        OnGenerated?.Invoke();
    }

    // Rolls every LootSpawnPoint in the placed parts and spawns prefabs at the winners,
    // one pass per category, honouring per-room/total caps and guaranteed minimums.
    private void SpawnRoomLoot()
    {
        // Bandwidth prefabs vary in size, so their pass rejects prefabs that don't
        // physically fit at a point and tries the others.
        int loot = SpawnCategory(LootSpawnPoint.Category.Bandwidth, _lootPrefabs,
            _lootChancePerPoint, _maxLootPerRoom, _maxTotalLoot, _minTotalLoot, mustFit: true);

        int cells = SpawnCategory(LootSpawnPoint.Category.EnergyCell, _energyCellPrefabs,
            _cellChancePerPoint, _maxCellsPerRoom, _maxTotalCells, _minTotalCells, mustFit: false);

        Debug.Log($"[DungeonGenerator] Spawned {loot} loot object(s) and {cells} energy cell(s) " +
                  $"across {_generatedRooms.Count} parts.");
    }

    // Runs the roll for one spawn-point category and returns how many objects were placed.
    private int SpawnCategory(LootSpawnPoint.Category category, List<GameObject> prefabs,
        float chancePerPoint, int maxPerRoom, int maxTotal, int minTotal, bool mustFit)
    {
        if (prefabs.Count == 0) return 0;

        int total = 0;
        // Points that lost their roll, kept around so the minimum can be topped up.
        List<(LootSpawnPoint point, DungeonPart part)> leftovers = new();
        Dictionary<DungeonPart, int> perRoomCount = new();

        foreach (DungeonPart part in _generatedRooms)
        {
            if (part == null) continue;
            if (maxTotal > 0 && total >= maxTotal) break;

            LootSpawnPoint[] points = System.Array.FindAll(
                part.GetComponentsInChildren<LootSpawnPoint>(),
                p => p.SpawnCategory == category);
            if (points.Length == 0) continue;

            int inRoom = 0;
            foreach (LootSpawnPoint point in WeightedOrder(points))
            {
                if (maxPerRoom > 0 && inRoom >= maxPerRoom) break;
                if (maxTotal > 0 && total >= maxTotal) break;

                if (UnityEngine.Random.value > chancePerPoint)
                {
                    leftovers.Add((point, part));
                    continue;
                }

                if (!SpawnLootAt(point, prefabs, mustFit)) continue; // nothing fits here
                inRoom++;
                total++;
            }

            perRoomCount[part] = inRoom;
        }

        // Short of the guaranteed minimum: fill random skipped points, still honouring caps.
        if (minTotal > 0 && total < minTotal && leftovers.Count > 0)
        {
            ShuffleList(leftovers);
            foreach ((LootSpawnPoint point, DungeonPart part) in leftovers)
            {
                if (total >= minTotal) break;
                if (maxTotal > 0 && total >= maxTotal) break;
                if (maxPerRoom > 0 && perRoomCount[part] >= maxPerRoom) continue;

                if (!SpawnLootAt(point, prefabs, mustFit)) continue;
                perRoomCount[part]++;
                total++;
            }
        }

        return total;
    }

    // Spawns one object at the point. With mustFit, a prefab whose colliders overlap the
    // world at the point is rejected and the remaining prefabs are tried (shuffled);
    // returns false when nothing fits, same instantiate-check-destroy pattern the room
    // placement itself uses.
    private bool SpawnLootAt(LootSpawnPoint point, List<GameObject> prefabs, bool mustFit)
    {
        if (!mustFit)
        {
            InstantiateLoot(PickRandom(prefabs), point);
            return true;
        }

        List<GameObject> order = new(prefabs);
        ShuffleList(order);

        foreach (GameObject prefab in order)
        {
            GameObject go = InstantiateLoot(prefab, point);
            if (!LootOverlapsSomething(go))
                return true;

            // Deactivate before the deferred Destroy so this candidate's colliders can't
            // block the next candidate's overlap check within the same frame.
            go.SetActive(false);
            Destroy(go);
        }

        Debug.Log($"[DungeonGenerator] No loot prefab fits at '{point.name}', skipping point.", point);
        return false;
    }

    private GameObject InstantiateLoot(GameObject prefab, LootSpawnPoint point)
    {
        Quaternion rot = _randomizeLootYRotation
            ? Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f)
            : point.transform.rotation;

        // Unparented, matching the RoverManager energy-cell drop: PurrNet auto-spawns
        // server-side instantiates, and runtime re-parenting under a networked room is
        // exactly the kind of hierarchy sync we don't want to depend on.
        GameObject go = Instantiate(prefab, point.transform.position, rot);
        go.transform.SetParent(null);

        // Hand ownership to the generator's cleanup sweeps (regeneration, restarts).
        if (go.TryGetComponent(out GrabbableObject grab))
            grab.ServerMarkAsDungeonLoot();

        return go;
    }

    // True if any of the object's solid colliders overlap the world at its current pose.
    // The test box is shrunk and lifted a touch so merely resting on the floor doesn't
    // read as a blocker, and DungeonPart room-bounds colliders are ignored since they
    // cover the entire interior.
    private static bool LootOverlapsSomething(GameObject go)
    {
        Physics.SyncTransforms();

        foreach (Collider col in go.GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger || !col.enabled) continue;

            Bounds b = col.bounds;
            Collider[] hits = Physics.OverlapBox(
                b.center + Vector3.up * 0.02f,
                b.extents * 0.9f,
                Quaternion.identity,
                ~0,
                QueryTriggerInteraction.Ignore);

            foreach (Collider hit in hits)
            {
                if (hit.transform.IsChildOf(go.transform)) continue;
                if (hit.TryGetComponent(out DungeonPart _)) continue;
                return true;
            }
        }

        return false;
    }

    // Orders points by weighted random draw (Efraimidis–Spirakis), so higher-weight points
    // come first and win when a room caps out, while equal weights are a fair shuffle.
    private static List<LootSpawnPoint> WeightedOrder(LootSpawnPoint[] points)
    {
        List<(float key, LootSpawnPoint point)> keyed = new(points.Length);
        foreach (LootSpawnPoint p in points)
        {
            float weight = Mathf.Max(0.0001f, p.Weight);
            keyed.Add((Mathf.Pow(UnityEngine.Random.value, 1f / weight), p));
        }
        keyed.Sort((a, b) => b.key.CompareTo(a.key));

        List<LootSpawnPoint> ordered = new(keyed.Count);
        foreach ((float _, LootSpawnPoint point) in keyed)
            ordered.Add(point);
        return ordered;
    }

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

        CleanupDungeonLoot(includeRoomChildren: true);

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

        foreach (GameObject wall in _spawnedFillerWalls)
        {
            if (wall != null)
                Destroy(wall);
        }

        _generatedRooms.Clear();
        _spawnedDoors.Clear();
        _spawnedFillerWalls.Clear();
        _consecutiveFailures = 0;
        _generationPaused = false;
        _tickTimer = 0f;
        isGenerated.value = false;
        // _shouldGenerate stays true so Update() resumes automatically next tick.
    }

    // Destroys never-picked-up dungeon loot. With includeRoomChildren, all untouched loot
    // goes; without it, only orphans left at the entrance by failed placements. Held loot
    // is always spared so items carried out survive.
    private void CleanupDungeonLoot(bool includeRoomChildren)
    {
        int removed = 0;

        foreach (GrabbableObject loot in FindObjectsByType<GrabbableObject>(FindObjectsSortMode.None))
        {
            if (!loot.IsDungeonLoot || loot.WasEverHeld) continue;
            if (!includeRoomChildren && loot.GetComponentInParent<DungeonPart>() != null) continue;

            Destroy(loot.gameObject);
            removed++;
        }

        if (removed > 0)
            Debug.Log($"[DungeonGenerator] Cleaned up {removed} stray dungeon loot object(s).");
    }

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

            // Only add to the list once we know it doesn't intersect.
            if (!HasIntersection(part, hostRoom))
            {
                _generatedRooms.Add(part);
                continue;
            }

            // Intersected: release and retry without touching _generatedRooms.
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

    // Tries the remaining entry point combinations to place partGo, adding it to the list
    // on success. On failure the caller must destroy the GameObjects.
    private bool RetryAttachment(GameObject partGo, GameObject doorGo, int depth = 0)
    {
        if (depth >= MaxPlacementRetries)
        {
            Debug.LogWarning("[DungeonGenerator] Max per-part retry depth reached, abandoning this part.");
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

            // Snap the door's NetworkTransform to its new pose, same spawn-snapshot race as the rooms.
            if (doorGo.TryGetComponent(out NetworkTransform doorNt))
                doorNt.ClearInterpolation(doorGo.transform.position, doorGo.transform.rotation, null);
        }

        if (HasIntersection(part, hostRoom))
        {
            part.ReleaseEntrypoint(newEntry);
            hostRoom.ReleaseEntrypoint(hostEntry);
            // Return the recursive result so a later success isn't reported as a failure.
            return RetryAttachment(partGo, doorGo, depth + 1);
        }

        // Valid position found: commit.
        _generatedRooms.Add(part);
        return true;
    }

    // Finds a placed room that still has a free entry point, shuffled to spread connections out.
    private bool TryFindAvailableRoom(out DungeonPart foundRoom, out Transform foundEntry)
    {
        foundRoom = null;
        foundEntry = null;

        List<DungeonPart> shuffled = new List<DungeonPart>(_generatedRooms);
        ShuffleList(shuffled);

        foreach (DungeonPart candidate in shuffled)
        {
            // Guard against destroyed parts still in the list mid-restart.
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

    // True if the part overlaps another placed DungeonPart; the host room is excluded
    // since touching at the connection point is expected.
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

        // A freshly-spawned NetworkTransform buffers its spawn pose, which can win the race
        // against this manual move. Clearing interpolation makes the aligned pose authoritative.
        if (newRoom.TryGetComponent(out NetworkTransform nt))
            nt.ClearInterpolation(newRoom.position, newRoom.rotation, null);
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