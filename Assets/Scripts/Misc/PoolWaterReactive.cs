using Unity.Netcode;
using UnityEngine;

public abstract class PoolWaterReactive : NetworkBehaviour
{
    public abstract void ApplyPoolWaterHit(
        WaterQuality waterQuality,
        float waterPower,
        Vector3 sourcePosition);
}
