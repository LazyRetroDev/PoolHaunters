using UnityEngine;
using UnityEngine.AI;

public class GhostWaterBehavior : MonoBehaviour
{
    enum GhostWaterState
    {
        Disguised,
        Revealed,
        Draining
    }

    [Header("Visual State")]
    public GameObject umbrellaVisualRoot;
    public GameObject trueFormVisualRoot;
    public GameObject rainVisualRoot;

    [Header("Movement")]
    public float disguisedSpeed = 1.6f;
    public float revealedSpeed = 3.5f;
    public float wanderRadius = 12f;
    public float destinationReachedDistance = 1.25f;
    public float rotationSpeed = 8f;

    [Header("Detection")]
    public float revealRange = 5f;
    public float chaseRange = 14f;
    public float loseInterestRange = 20f;
    public LayerMask playerMask = ~0;
    public LayerMask lineOfSightMask = ~0;
    public bool requireLineOfSightToReveal = true;

    [Header("Contaminated Rain")]
    public float rainRadius = 2.75f;
    public float rainInterval = 0.5f;
    public bool contaminatePlayerWater = true;
    public bool contaminateWaterSources = true;
    public GameObject contaminationZonePrefab;
    public float contaminationZoneInterval = 1.25f;

    [Header("Swallow / Drain")]
    public float swallowRange = 1.7f;
    public float drainDamagePerSecond = 18f;
    public float drainLockDuration = 1.5f;
    public bool stopMovingWhileDraining = true;

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float revealedVignette = 0.35f;
    [Range(0f, 1f)] public float drainingVignette = 0.75f;
    public float revealShakeAmplitude = 0.45f;
    public float revealShakeFrequency = 9f;
    public float revealShakeDuration = 0.3f;

    private NavMeshAgent agent;
    private Transform targetPlayer;
    private PlayerStatus targetStatus;
    private GhostWaterState state = GhostWaterState.Disguised;
    private float rainTimer;
    private float contaminationZoneTimer;
    private float drainTimer;
    private bool revealEffectPlayed;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        ResolveCameraEffects();
        UpdateVisuals();
        SetWanderDestination(disguisedSpeed);
    }

    void Update()
    {
        ResolveCameraEffects();
        UpdateTarget();
        UpdateRain();

        switch (state)
        {
            case GhostWaterState.Disguised:
                UpdateDisguised();
                break;
            case GhostWaterState.Revealed:
                UpdateRevealed();
                break;
            case GhostWaterState.Draining:
                UpdateDraining();
                break;
        }
    }

    void UpdateTarget()
    {
        PlayerStatus[] players = FindObjectsOfType<PlayerStatus>();
        float bestDistance = float.PositiveInfinity;
        targetPlayer = null;
        targetStatus = null;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus status = players[i];
            if (status == null || status.IsDead()) continue;

            float distance = Vector3.Distance(transform.position, status.transform.position);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            targetPlayer = status.transform;
            targetStatus = status;
        }
    }

    void UpdateDisguised()
    {
        Wander(disguisedSpeed);

        if (targetPlayer == null) return;
        if (Vector3.Distance(transform.position, targetPlayer.position) > revealRange) return;
        if (requireLineOfSightToReveal && !HasLineOfSight(targetPlayer)) return;

        Reveal();
    }

    void UpdateRevealed()
    {
        if (targetPlayer == null)
        {
            Wander(revealedSpeed);
            UpdateCameraEffect(0f);
            return;
        }

        float distance = Vector3.Distance(transform.position, targetPlayer.position);
        UpdateCameraEffect(distance);

        if (distance > loseInterestRange)
        {
            Wander(revealedSpeed);
            return;
        }

        MoveTo(targetPlayer.position, revealedSpeed);

        if (distance <= swallowRange)
            BeginDraining();
    }

    void UpdateDraining()
    {
        if (targetPlayer == null || targetStatus == null)
        {
            ChangeState(GhostWaterState.Revealed);
            return;
        }

        FaceTarget(targetPlayer.position);
        UpdateCameraEffect(Vector3.Distance(transform.position, targetPlayer.position));

        if (stopMovingWhileDraining)
            StopAgent();
        else
            MoveTo(targetPlayer.position, revealedSpeed);

        drainTimer -= Time.deltaTime;
        targetStatus.TakeDamage(drainDamagePerSecond * Time.deltaTime);
        if (contaminatePlayerWater)
            targetStatus.ContaminateWater();

        float distance = Vector3.Distance(transform.position, targetPlayer.position);
        if (drainTimer <= 0f || distance > swallowRange * 1.35f || targetStatus.IsDead())
            ChangeState(GhostWaterState.Revealed);
    }

    void Reveal()
    {
        ChangeState(GhostWaterState.Revealed);

        if (revealEffectPlayed) return;
        revealEffectPlayed = true;

        if (cameraEffects != null)
        {
            cameraEffects.Pulse(revealedVignette, revealShakeDuration);
            cameraEffects.Shake(revealShakeAmplitude, revealShakeFrequency, revealShakeDuration);
        }
    }

    void BeginDraining()
    {
        drainTimer = drainLockDuration;
        ChangeState(GhostWaterState.Draining);
    }

    void UpdateRain()
    {
        rainTimer -= Time.deltaTime;
        contaminationZoneTimer -= Time.deltaTime;

        if (rainTimer <= 0f)
        {
            rainTimer = rainInterval;
            ContaminateInRainRadius();
        }

        if (contaminationZonePrefab != null && contaminationZoneTimer <= 0f)
        {
            contaminationZoneTimer = contaminationZoneInterval;
            Instantiate(contaminationZonePrefab, transform.position, Quaternion.identity);
        }
    }

    void ContaminateInRainRadius()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            rainRadius,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null) continue;

            if (contaminatePlayerWater)
            {
                PlayerStatus status = hit.GetComponentInParent<PlayerStatus>();
                if (status != null)
                    status.ContaminateWater();
            }

            if (contaminateWaterSources)
            {
                WaterSourceDryable source = hit.GetComponentInParent<WaterSourceDryable>();
                if (source != null)
                    source.Contaminate();
            }
        }
    }

    bool HasLineOfSight(Transform target)
    {
        if (target == null) return false;

        Vector3 origin = transform.position + Vector3.up * 1.2f;
        Vector3 targetPoint = target.position + Vector3.up * 1.2f;
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;
        if (distance <= 0.01f) return true;

        RaycastHit hit;
        if (!Physics.Raycast(
            origin,
            direction.normalized,
            out hit,
            distance,
            lineOfSightMask,
            QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return hit.collider != null && hit.collider.GetComponentInParent<PlayerStatus>() != null;
    }

    void Wander(float speed)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        agent.speed = speed;
        if (!agent.pathPending && agent.remainingDistance <= destinationReachedDistance)
            SetWanderDestination(speed);
    }

    void SetWanderDestination(float speed)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius + transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, wanderRadius, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.speed = speed;
            agent.SetDestination(hit.position);
        }
    }

    void MoveTo(Vector3 target, float speed)
    {
        FaceTarget(target);

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = speed;
            agent.SetDestination(target);
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    void StopAgent()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = true;
    }

    void ChangeState(GhostWaterState nextState)
    {
        if (state == nextState) return;
        state = nextState;
        UpdateVisuals();
    }

    void UpdateVisuals()
    {
        bool disguised = state == GhostWaterState.Disguised;

        if (umbrellaVisualRoot != null)
            umbrellaVisualRoot.SetActive(disguised);

        if (trueFormVisualRoot != null)
            trueFormVisualRoot.SetActive(!disguised);

        if (rainVisualRoot != null)
            rainVisualRoot.SetActive(true);
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindObjectOfType<PlayerVignetteEffect>();
    }

    void UpdateCameraEffect(float distanceToPlayer)
    {
        if (cameraEffects == null) return;

        if (state == GhostWaterState.Disguised)
        {
            cameraEffects.ClearThreatIntensity();
            return;
        }

        float range = Mathf.Max(0.01f, chaseRange);
        float danger = 1f - Mathf.Clamp01(distanceToPlayer / range);
        float maxIntensity = state == GhostWaterState.Draining ? drainingVignette : revealedVignette;
        cameraEffects.SetThreatIntensity(maxIntensity * danger);
    }

    void OnDestroy()
    {
        if (cameraEffects != null)
            cameraEffects.ClearThreatIntensity();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, rainRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, revealRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, swallowRange);
    }
}
