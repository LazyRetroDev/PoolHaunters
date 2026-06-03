using UnityEngine;

[RequireComponent(typeof(Item))]
public class WaterUsagePowerupItem : UsableItem
{
    public float waterUsageMultiplier = 0.5f;
    public float duration = 10f;

    public override bool Use(PlayerInventory inventory, PlayerStatus playerStatus)
    {
        if (inventory == null) return false;

        WaterCannon waterCannon = inventory.GetComponentInChildren<WaterCannon>();
        if (waterCannon == null) return false;

        waterCannon.ApplyWaterUsageMultiplier(waterUsageMultiplier, duration);
        return true;
    }
}
