using UnityEngine;

/// <summary>
/// Upgrade that adds a rolled amount to the player's walk speed.
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Walk Speed", fileName = "Upgrade_WalkSpeed")]
public class WalkSpeedUpgrade : UpgradeDefinition
{
    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerAddWalkSpeed(rolledValue);

    protected override string FormatValue(float v) => "+" + v.ToString("0.##");
}
