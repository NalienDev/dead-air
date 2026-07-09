using UnityEngine;

/// <summary>Not repeatable (by default). Grants one extra inventory slot.</summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Inventory Slot", fileName = "Upgrade_InventorySlot")]
public class InventorySlotUpgrade : UpgradeDefinition
{
    [Tooltip("How many slots this grants.")]
    [SerializeField] private int _slots = 1;

    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerAddInventorySlots(_slots);
}
