using UnityEngine;

/// <summary>
/// Repeatable. Increases max oxygen by a random percentage. Store the range as
/// fractions in ValueRange (e.g. 0.1 → 0.5 for 10%–50%).
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Oxygen Percent", fileName = "Upgrade_OxygenPercent")]
public class OxygenPercentUpgrade : UpgradeDefinition
{
    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerAddOxygenPercent(rolledValue);

    // 0.25 → "+25%"
    protected override string FormatValue(float v) => "+" + Mathf.RoundToInt(v * 100f) + "%";
}
