using UnityEngine;

/// <summary>
/// Grants extra oxygen-station charges: raises the player's max (so expedition resets
/// keep the higher count) and hands the new charges over immediately.
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Oxygen Charges", fileName = "Upgrade_OxygenCharges")]
public class OxygenChargesUpgrade : UpgradeDefinition
{
    [Tooltip("How many extra station charges this grants.")]
    [SerializeField] private int _charges = 1;

    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerAddOxygenCharges(_charges);
}
