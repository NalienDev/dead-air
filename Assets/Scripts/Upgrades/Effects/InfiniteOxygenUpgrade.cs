using UnityEngine;

/// <summary>
/// Upgrade that stops the player's oxygen from draining.
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Infinite Oxygen", fileName = "Upgrade_InfiniteOxygen")]
public class InfiniteOxygenUpgrade : UpgradeDefinition
{
    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerGrantInfiniteOxygen();
}
