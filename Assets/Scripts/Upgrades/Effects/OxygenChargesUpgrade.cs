using UnityEngine;

/// <summary>
/// Upgrade that grants extra oxygen-station charges and raises the player's max.
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Oxygen Charges", fileName = "Upgrade_OxygenCharges")]
public class OxygenChargesUpgrade : UpgradeDefinition
{
    [Tooltip("How many extra station charges this grants.")]
    [SerializeField] private int _charges = 1;

    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerAddOxygenCharges(_charges);
}
