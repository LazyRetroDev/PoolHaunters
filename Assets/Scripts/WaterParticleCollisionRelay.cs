using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class WaterParticleCollisionRelay : MonoBehaviour
{
    void OnParticleCollision(GameObject other)
    {
        if (other == null) return;

        RaccoonBehavior raccoon = other.GetComponentInParent<RaccoonBehavior>();
        if (raccoon != null)
            raccoon.ReceiveWaterHit(transform.position);
    }
}
