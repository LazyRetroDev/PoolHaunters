using UnityEngine;

public class WaterZone : MonoBehaviour
{
    public WaterSourceDryable waterSource;
    public bool allowInfiniteWaterWhenNoSource = true;
    public WaterQuality fallbackWaterQuality = WaterQuality.Clean;
    public bool replacePlayerWaterQuality = false;

    void Awake()
    {
        if (waterSource == null)
            waterSource = GetComponentInParent<WaterSourceDryable>();
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerStatus status = other.GetComponent<PlayerStatus>();
        if (status != null)
            status.SetWaterZone(this);
    }

    void OnTriggerExit(Collider other)
    {
        PlayerStatus status = other.GetComponent<PlayerStatus>();
        if (status != null)
            status.ClearWaterZone(this);
    }

    public bool TryFillPlayer(PlayerStatus status, float requestedAmount)
    {
        if (status == null || requestedAmount <= 0f) return false;

        float neededWater = Mathf.Min(requestedAmount, status.GetWaterSpace());
        if (neededWater <= 0f) return false;

        if (waterSource == null)
        {
            if (!allowInfiniteWaterWhenNoSource) return false;
            return status.AddWater(neededWater, fallbackWaterQuality, replacePlayerWaterQuality);
        }

        float drainedAmount = waterSource.DrainWater(neededWater, out WaterQuality sourceQuality);
        if (drainedAmount <= 0f) return false;

        return status.AddWater(drainedAmount, sourceQuality, waterSource.replacePlayerWaterQuality);
    }
}
