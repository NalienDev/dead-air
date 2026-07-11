using UnityEngine;

/// <summary>
/// Upgrade that increases max oxygen by a rolled percentage.
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Oxygen Percent", fileName = "Upgrade_OxygenPercent")]
public class OxygenPercentUpgrade : UpgradeDefinition
{
    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerAddOxygenPercent(rolledValue);

    protected override string FormatValue(float v) => "+" + Mathf.RoundToInt(v * 100f) + "%";
}
