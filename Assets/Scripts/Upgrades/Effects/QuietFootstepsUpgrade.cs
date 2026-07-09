using UnityEngine;

/// <summary>
/// Reduces the player's footstep/movement noise by a rolled fraction, making them
/// harder for the blind Conductor to hear. Set ValueRange as fractions
/// (e.g. 0.2 → 0.4 for 20%–40% quieter). Stacks multiplicatively if repeatable.
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Quiet Footsteps", fileName = "Upgrade_QuietFootsteps")]
public class QuietFootstepsUpgrade : UpgradeDefinition
{
    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerReduceFootstepNoise(rolledValue);

    // 0.3 → "-30% noise"
    protected override string FormatValue(float v) => "-" + Mathf.RoundToInt(v * 100f) + "% noise";
}
