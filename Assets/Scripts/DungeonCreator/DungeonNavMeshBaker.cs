using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Rebakes the dungeon NavMesh asynchronously every time generation finishes.
/// </summary>
[RequireComponent(typeof(NavMeshSurface))]
public class DungeonNavMeshBaker : MonoBehaviour
{
    private NavMeshSurface _surface;
    private bool _baking;
    private bool _bakeQueued;

    private void Awake() => _surface = GetComponent<NavMeshSurface>();

    private void Start()
    {
        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.OnGenerated += RequestBake;
    }

    private void OnDestroy()
    {
        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.OnGenerated -= RequestBake;
    }

    private void RequestBake()
    {
        if (_baking) { _bakeQueued = true; return; }
        StartCoroutine(BakeAsync());
    }

    private IEnumerator BakeAsync()
    {
        _baking = true;

        NavMeshSurface[] surfaces = FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (surfaces.Length > 1)
            Debug.LogWarning($"[DungeonNavMeshBaker] {surfaces.Length} NavMeshSurfaces in the scene — " +
                             "another surface may be holding a stale NavMesh that this baker never clears.");

        // Volume collection re-places the mesh with position and rotation only, so any
        // scale or rotation on this transform shifts the NavMesh off the real geometry.
        if (transform.lossyScale != Vector3.one || transform.rotation != Quaternion.identity)
            Debug.LogWarning($"[DungeonNavMeshBaker] Surface transform is not identity " +
                             $"(rot {transform.rotation.eulerAngles}, scale {transform.lossyScale}) — " +
                             "the baked NavMesh will be misplaced. Reset rotation/scale and size the " +
                             "volume via the NavMeshSurface's Center/Size fields instead.");

        // Let this frame settle first: OnGenerated fires in the same frame that spawns
        // filler walls and queues Destroy()s of rejected parts.
        yield return null;

        // Fresh data every bake, otherwise updating in place leaves stale mesh from the
        // previous dungeon layout behind.
        _surface.RemoveData();
        _surface.navMeshData = new NavMeshData(_surface.agentTypeID);
        _surface.AddData();

        // Diagnostic: where the dungeon geometry currently is.
        Bounds dungeonBounds = new Bounds();
        bool first = true;
        foreach (DungeonPart room in DungeonGenerator.Instance.GetGeneratedRooms())
        {
            if (room == null || room.collider == null) continue;
            if (first) { dungeonBounds = room.collider.bounds; first = false; }
            else dungeonBounds.Encapsulate(room.collider.bounds);
        }
        Debug.Log($"[DungeonNavMeshBaker] Baking frame {Time.frameCount}: " +
                  $"{DungeonGenerator.Instance.GetGeneratedRooms().Count} rooms, dungeon bounds center {dungeonBounds.center}. " +
                  $"Surface: collect={_surface.collectObjects}, geometry={_surface.useGeometry}, layers={_surface.layerMask.value}");

        yield return _surface.UpdateNavMesh(_surface.navMeshData);

        _baking = false;

        // Diagnostic: where the baked mesh ended up.
        Debug.Log($"[DungeonNavMeshBaker] Bake done frame {Time.frameCount}: " +
                  $"NavMesh source bounds center {_surface.navMeshData.sourceBounds.center}, " +
                  $"size {_surface.navMeshData.sourceBounds.size}");

        // The map regenerated while we were baking, so bake again with the new layout.
        if (_bakeQueued)
        {
            _bakeQueued = false;
            RequestBake();
        }
    }
}
