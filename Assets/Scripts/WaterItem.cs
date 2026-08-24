using UnityEngine;

[RequireComponent(typeof(Item))]
public class WaterItem : MonoBehaviour
{
    [Header("Water Effect")]
    public float waterAmount = 25f;
    public WaterQuality waterQuality = WaterQuality.Clean;
    public bool replaceExistingWaterQuality = false;

    [Header("Pickup Behavior")]
    public bool useImmediatelyOnPickup = true;
    public bool destroyAfterUse = true;

    public bool TryApply(
        PlayerStatus playerStatus,
        bool ignoreControlLock = false)
    {
        if (playerStatus == null || waterAmount <= 0f) return false;
        if (ignoreControlLock)
        {
            return playerStatus.AddWaterIgnoringControlLock(
                waterAmount,
                waterQuality,
                replaceExistingWaterQuality);
        }

        return playerStatus.AddWater(waterAmount, waterQuality, replaceExistingWaterQuality);
    }
}
