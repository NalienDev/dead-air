using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source of truth for every upgrade, where each one's list index is its network id.
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Database", fileName = "UpgradeDatabase")]
public class UpgradeDatabase : ScriptableObject
{
    [Tooltip("Every upgrade that can appear. The index is the id sent over the network, so don't reorder mid-playtest.")]
    [SerializeField] private List<UpgradeDefinition> _upgrades = new();

    public int Count => _upgrades.Count;

    public UpgradeDefinition Get(int index) =>
        (index >= 0 && index < _upgrades.Count) ? _upgrades[index] : null;

    public int IndexOf(UpgradeDefinition def) => _upgrades.IndexOf(def);

    // Rolls up to count distinct upgrade options, honouring requirements, rarity, and weight.
    public int[] RollOptions(int count, PlayerUpgrades player, int expeditionsCompleted)
    {
        var pool = new List<int>();
        var weights = new List<float>();

        for (int i = 0; i < _upgrades.Count; i++)
        {
            UpgradeDefinition d = _upgrades[i];
            if (d == null) continue;
            if (expeditionsCompleted < d.MinExpeditions) continue;
            if (!d.Repeatable && player != null && player.HasUpgrade(i)) continue;
            // Super-rare independent gate, rolled per offer.
            if (d.AppearChance < 1f && Random.value > d.AppearChance) continue;

            pool.Add(i);
            weights.Add(Mathf.Max(0.0001f, d.Weight));
        }

        var result = new List<int>();
        for (int n = 0; n < count && pool.Count > 0; n++)
        {
            int pick = WeightedPick(weights);
            result.Add(pool[pick]);
            pool.RemoveAt(pick);
            weights.RemoveAt(pick);
        }
        return result.ToArray();
    }

    // Every upgrade, ignoring requirements and rarity; used by the machine's debug mode.
    public int[] AllOptions()
    {
        var all = new List<int>();
        for (int i = 0; i < _upgrades.Count; i++)
            if (_upgrades[i] != null) all.Add(i);
        return all.ToArray();
    }

    private static int WeightedPick(List<float> weights)
    {
        float total = 0f;
        foreach (float w in weights) total += w;

        float roll = Random.value * total;
        for (int i = 0; i < weights.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0f) return i;
        }
        return weights.Count - 1;
    }
}
