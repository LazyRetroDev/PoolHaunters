using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(ParticleSystem))]
public class WaterParticleCollisionRelay : MonoBehaviour
{
    [Tooltip("Extra radius around each visible water particle used to detect water-reactive enemies.")]
    public float particleHitRadius = 0.35f;

    [Tooltip("Layers containing enemies that can react to water.")]
    public LayerMask waterReactiveLayers = ~0;

    private const int InitialParticleCapacity = 256;
    private const int ColliderBufferSize = 16;

    private ParticleSystem waterParticles;
    private ParticleSystem.Particle[] particleBuffer;
    private readonly Collider[] colliderBuffer = new Collider[ColliderBufferSize];
    private PlayerMovement ownerMovement;

    void Awake()
    {
        waterParticles = GetComponent<ParticleSystem>();
        particleBuffer = new ParticleSystem.Particle[InitialParticleCapacity];
        ownerMovement = GetComponentInParent<PlayerMovement>();
    }

    void Update()
    {
        if (!CanProcessGameplayCollision())
            return;

        DetectWaterReactiveEnemiesInsideParticles();
    }

    void OnParticleCollision(GameObject other)
    {
        if (!CanProcessGameplayCollision())
            return;

        NotifyWaterReactiveEnemy(other);
    }

    void DetectWaterReactiveEnemiesInsideParticles()
    {
        if (waterParticles == null || !waterParticles.isPlaying)
            return;

        int livingParticleCount = waterParticles.particleCount;
        if (livingParticleCount <= 0)
            return;

        if (particleBuffer.Length < livingParticleCount)
            particleBuffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(livingParticleCount)];

        int particleCount = waterParticles.GetParticles(particleBuffer);
        ParticleSystem.MainModule main = waterParticles.main;

        for (int i = 0; i < particleCount; i++)
        {
            Vector3 particlePosition = GetWorldPosition(particleBuffer[i], main);
            float radius = particleHitRadius + particleBuffer[i].GetCurrentSize(waterParticles) * 0.5f;
            int hitCount = Physics.OverlapSphereNonAlloc(
                particlePosition,
                radius,
                colliderBuffer,
                waterReactiveLayers,
                QueryTriggerInteraction.Collide);

            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                NotifyWaterReactiveEnemy(colliderBuffer[hitIndex].gameObject);
        }
    }

    Vector3 GetWorldPosition(ParticleSystem.Particle particle, ParticleSystem.MainModule main)
    {
        switch (main.simulationSpace)
        {
            case ParticleSystemSimulationSpace.Local:
                return transform.TransformPoint(particle.position);
            case ParticleSystemSimulationSpace.Custom:
                return main.customSimulationSpace != null
                    ? main.customSimulationSpace.TransformPoint(particle.position)
                    : particle.position;
            default:
                return particle.position;
        }
    }

    void NotifyWaterReactiveEnemy(GameObject hitObject)
    {
        if (hitObject == null)
            return;

        if (ownerMovement != null)
        {
            ownerMovement.RequestEnemyWaterHit(hitObject, transform.position);
            return;
        }

        RaccoonBehavior raccoon = hitObject.GetComponentInParent<RaccoonBehavior>();
        if (raccoon != null)
            raccoon.ReceiveWaterHit(transform.position);

        BathroomBlondeBehavior bathroomBlonde = hitObject.GetComponentInParent<BathroomBlondeBehavior>();
        if (bathroomBlonde != null)
            bathroomBlonde.ReceiveWaterHit(transform.position);

        BathroomBlondeMirror bathroomMirror = hitObject.GetComponentInParent<BathroomBlondeMirror>();
        if (bathroomMirror != null)
            bathroomMirror.ReceiveWaterHit(transform.position);

        BathroomBlondeDrain bathroomDrain = hitObject.GetComponentInParent<BathroomBlondeDrain>();
        if (bathroomDrain != null)
            bathroomDrain.ReceiveWaterHit(transform.position);
    }

    bool CanProcessGameplayCollision()
    {
        if (ownerMovement == null)
            return true;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null ||
            !networkManager.IsListening ||
            !ownerMovement.IsSpawned)
        {
            return true;
        }

        return ownerMovement.IsOwner;
    }
}
