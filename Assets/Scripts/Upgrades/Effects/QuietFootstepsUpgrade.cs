using UnityEngine;

/// <summary>
/// Upgrade that reduces the player's footstep noise, making them harder for the Conductor to hear.
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Quiet Footsteps", fileName = "Upgrade_QuietFootsteps")]
public class QuietFootstepsUpgrade : UpgradeDefinition
{
    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerReduceFootstepNoise(rolledValue);

    protected override string FormatValue(float v) => "-" + Mathf.RoundToInt(v * 100f) + "% noise";
}
