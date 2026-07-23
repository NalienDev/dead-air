using UnityEngine;

/// <summary>
/// Marker for a candidate loot spawn position inside a dungeon part prefab.
/// After generation, the DungeonGenerator rolls each point to decide whether
/// an object of the point's category (bandwidth loot or energy cell) spawns there.
/// </summary>
public class LootSpawnPoint : MonoBehaviour
{
    public enum Category { Bandwidth, EnergyCell }

    [Tooltip("What kind of loot can spawn here. Bandwidth = quota objects, EnergyCell = dampener cells.")]
    [SerializeField] private Category _category = Category.Bandwidth;

    [Tooltip("Points with higher weight are preferred when a room caps out its loot.")]
    [SerializeField, Min(0f)] private float _weight = 1f;

    public Category SpawnCategory => _category;
    public float Weight => _weight;

    private void OnDrawGizmos()
    {
        // Cyan = bandwidth loot, yellow = energy cell, so categories read at a glance.
        Gizmos.color = _category == Category.EnergyCell ? Color.yellow : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.25f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.5f);
    }
}
