using UnityEngine;

public abstract class PoolWaterReactive : MonoBehaviour
{
    public abstract void ApplyPoolWaterHit(
        WaterQuality waterQuality,
        float waterPower,
        Vector3 sourcePosition);
}
