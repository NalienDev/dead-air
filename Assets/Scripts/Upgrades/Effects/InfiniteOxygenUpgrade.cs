using UnityEngine;

/// <summary>
/// Not repeatable, super rare (set AppearChance ≈ 0.01). Oxygen stops draining.
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Infinite Oxygen", fileName = "Upgrade_InfiniteOxygen")]
public class InfiniteOxygenUpgrade : UpgradeDefinition
{
    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerGrantInfiniteOxygen();
}
