using UnityEngine;

[RequireComponent(typeof(Item))]
public class WaterFilterItem : UsableItem
{
    public override bool Use(PlayerInventory inventory, PlayerStatus playerStatus)
    {
        if (playerStatus == null) return false;
        if (playerStatus.GetCurrentWater() <= 0f) return false;
        if (playerStatus.GetWaterQuality() != WaterQuality.Contaminated) return false;

        playerStatus.PurifyWater();
        return true;
    }
}
