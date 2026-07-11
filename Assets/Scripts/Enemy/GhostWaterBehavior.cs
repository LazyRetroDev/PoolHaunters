using UnityEngine;
using UnityEngine.AI;

public class GhostWaterBehavior : MonoBehaviour
{
    enum GhostWaterState
    {
        Disguised,
        Revealed,
        Swallowing
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

    [Header("Disguise")]
    public bool canReturnToDisguise = true;
    public float redisguiseDelay = 4f;
    public float minimumRevealedTime = 2f;
    public bool hideRainWhileDisguised = false;

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

    [Header("Swallow")]
    public float swallowRange = 1.7f;
    public float swallowDuration = 2.5f;
    public float swallowDamagePerSecond = 18f;
    public Transform swallowHoldPoint;
    public Vector3 swallowHoldOffset = new Vector3(0f, 1.1f, 0.45f);
    public bool blockPlayerControlsWhileSwallowed = true;
    public bool contaminatePlayerWaterOnSwallow = true;

    [Header("Teleport After Swallow")]
    public bool teleportPlayerAfterSwallow = true;
    public Transform[] teleportDropPoints;
    public float fallbackTeleportDistance = 8f;
    public float teleportNavMeshSampleRadius = 5f;
    public Vector3 teleportGroundOffset = new Vector3(0f, 0.15f, 0f);
    public bool returnToDisguiseAfterSwallow = true;

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float revealedVignette = 0.35f;
    [Range(0f, 1f)] public float swallowingVignette = 0.75f;
    public float revealShakeAmplitude = 0.45f;
    public float revealShakeFrequency = 9f;
    public float revealShakeDuration = 0.3f;
    public float swallowShakeAmplitude = 0.9f;
    public float swallowShakeFrequency = 13f;
    public float swallowShakeDuration = 0.35f;

    private NavMeshAgent agent;
    private Transform targetPlayer;
    private PlayerStatus targetStatus;
    private GhostWaterState state = GhostWaterState.Disguised;
    private float rainTimer;
    private float contaminationZoneTimer;
    private float revealedTimer;
    private float redisguiseTimer;
    private float swallowTimer;
    private PlayerStatus swallowedStatus;
    private Transform swallowedPlayer;
    private PlayerMovement swallowedMovement;
    private PlayerInventory swallowedInventory;
    private WaterCannon swallowedWaterCannon;
    private Rigidbody swallowedRigidbody;
    private bool swallowedMovementWasEnabled;
    private bool swallowedInventoryWasEnabled;
    private bool swallowedWaterCannonWasEnabled;
    private bool swallowedRigidbodyWasKinematic;
    private bool swallowedRigidbodyUsedGravity;
    private bool revealEffectPlayed;
    private PlayerMovement effectTargetMovement;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        ResolveCameraEffects();
        UpdateVisuals();
        SetWanderDestination(disguisedSpeed);
    }

    void Update()
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        ResolveCameraEffects();

        if (state != GhostWaterState.Swallowing)
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
            case GhostWaterState.Swallowing:
                UpdateSwallowing();
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
            if ((playerMask.value & (1 << status.gameObject.layer)) == 0) continue;

            bestDistance = distance;
            targetPlayer = status.transform;
            targetStatus = status;
        }
    }

    void UpdateDisguised()
    {
        Wander(disguisedSpeed);
        UpdateCameraEffect(0f);

        if (targetPlayer == null) return;
        if (Vector3.Distance(transform.position, targetPlayer.position) > revealRange) return;
        if (requireLineOfSightToReveal && !HasLineOfSight(targetPlayer)) return;

        Reveal();
    }

    void UpdateRevealed()
    {
        revealedTimer += Time.deltaTime;

        if (targetPlayer == null)
        {
            Wander(revealedSpeed);
            TryReturnToDisguise();
            return;
        }

        float distance = Vector3.Distance(transform.position, targetPlayer.position);
        UpdateCameraEffect(distance);

        if (distance > loseInterestRange)
        {
            Wander(revealedSpeed);
            TryReturnToDisguise();
            return;
        }

        redisguiseTimer = redisguiseDelay;
        MoveTo(targetPlayer.position, revealedSpeed);

        if (distance <= swallowRange)
            BeginSwallow(targetStatus);
    }

    void TryReturnToDisguise()
    {
        if (!canReturnToDisguise) return;
        if (revealedTimer < minimumRevealedTime) return;

        redisguiseTimer -= Time.deltaTime;
        if (redisguiseTimer > 0f) return;

        ChangeState(GhostWaterState.Disguised);
        revealEffectPlayed = false;
        SetWanderDestination(disguisedSpeed);
    }

    void BeginSwallow(PlayerStatus victim)
    {
        if (victim == null || victim.IsDead()) return;

        swallowedStatus = victim;
        swallowedPlayer = victim.transform;
        swallowedMovement = victim.GetComponent<PlayerMovement>();
        swallowedInventory = victim.GetComponent<PlayerInventory>();
        swallowedWaterCannon = victim.GetComponentInChildren<WaterCannon>();
        swallowedRigidbody = victim.GetComponent<Rigidbody>();

        StoreAndBlockVictimControls();

        swallowTimer = swallowDuration;
        ChangeState(GhostWaterState.Swallowing);
        StopAgent();

        EnemyPlayerEffects.Pulse(
            ref effectTargetMovement,
            swallowedStatus,
            swallowedPlayer,
            cameraEffects,
            swallowingVignette,
            swallowShakeDuration,
            swallowShakeAmplitude,
            swallowShakeFrequency,
            swallowShakeDuration);
    }

    void UpdateSwallowing()
    {
        if (swallowedStatus == null || swallowedPlayer == null || swallowedStatus.IsDead())
        {
            EndSwallow(false);
            return;
        }

        StopAgent();
        HoldSwallowedPlayer();
        FaceTarget(swallowedPlayer.position);
        UpdateCameraEffect(0f);

        swallowTimer -= Time.deltaTime;
        swallowedStatus.TakeDamage(swallowDamagePerSecond * Time.deltaTime);

        if (contaminatePlayerWaterOnSwallow || contaminatePlayerWater)
            swallowedStatus.ContaminateWater();

        if (swallowTimer <= 0f || swallowedStatus.IsDead())
            EndSwallow(true);
    }

    void EndSwallow(bool teleportVictim)
    {
        PlayerStatus releasedStatus = swallowedStatus;
        Transform releasedPlayer = swallowedPlayer;

        if (teleportVictim && teleportPlayerAfterSwallow && releasedPlayer != null)
            TeleportPlayer(releasedPlayer);

        RestoreVictimControls(releasedStatus);
        ClearSwallowedReferences();

        if (returnToDisguiseAfterSwallow && canReturnToDisguise)
        {
            ChangeState(GhostWaterState.Disguised);
            revealEffectPlayed = false;
            SetWanderDestination(disguisedSpeed);
            return;
        }

        ChangeState(GhostWaterState.Revealed);
        redisguiseTimer = redisguiseDelay;
    }

    void StoreAndBlockVictimControls()
    {
        if (!blockPlayerControlsWhileSwallowed) return;

        if (swallowedStatus != null)
            swallowedStatus.AddExternalControlLock();

        if (swallowedMovement != null)
        {
            swallowedMovementWasEnabled = swallowedMovement.enabled;
            swallowedMovement.enabled = false;
        }

        if (swallowedInventory != null)
        {
            swallowedInventoryWasEnabled = swallowedInventory.enabled;
            swallowedInventory.enabled = false;
        }

        if (swallowedWaterCannon != null)
        {
            swallowedWaterCannonWasEnabled = swallowedWaterCannon.enabled;
            swallowedWaterCannon.enabled = false;
        }

        if (swallowedRigidbody != null)
        {
            swallowedRigidbodyWasKinematic = swallowedRigidbody.isKinematic;
            swallowedRigidbodyUsedGravity = swallowedRigidbody.useGravity;
            swallowedRigidbody.linearVelocity = Vector3.zero;
            swallowedRigidbody.angularVelocity = Vector3.zero;
            swallowedRigidbody.useGravity = false;
            swallowedRigidbody.isKinematic = true;
        }
    }

    void RestoreVictimControls(PlayerStatus releasedStatus)
    {
        if (!blockPlayerControlsWhileSwallowed) return;

        if (releasedStatus != null)
            releasedStatus.RemoveExternalControlLock();

        bool canRestore = releasedStatus != null && releasedStatus.CanAct();

        if (swallowedMovement != null)
            swallowedMovement.enabled = canRestore && swallowedMovementWasEnabled;

        if (swallowedInventory != null)
            swallowedInventory.enabled = canRestore && swallowedInventoryWasEnabled;

        if (swallowedWaterCannon != null)
            swallowedWaterCannon.enabled = canRestore && swallowedWaterCannonWasEnabled;

        if (swallowedRigidbody != null && canRestore)
        {
            swallowedRigidbody.isKinematic = swallowedRigidbodyWasKinematic;
            swallowedRigidbody.useGravity = swallowedRigidbodyUsedGravity;
        }
    }

    void ClearSwallowedReferences()
    {
        swallowedStatus = null;
        swallowedPlayer = null;
        swallowedMovement = null;
        swallowedInventory = null;
        swallowedWaterCannon = null;
        swallowedRigidbody = null;
        swallowedMovementWasEnabled = false;
        swallowedInventoryWasEnabled = false;
        swallowedWaterCannonWasEnabled = false;
    }

    void HoldSwallowedPlayer()
    {
        if (swallowedPlayer == null) return;

        Vector3 holdPosition = swallowHoldPoint != null
            ? swallowHoldPoint.position
            : transform.TransformPoint(swallowHoldOffset);

        TeleportTransform(swallowedPlayer, holdPosition, transform.rotation);
    }

    void TeleportPlayer(Transform playerTransform)
    {
        Vector3 destination = GetTeleportDestination();
        TeleportTransform(playerTransform, destination, playerTransform.rotation);
    }

    Vector3 GetTeleportDestination()
    {
        Transform dropPoint = ChooseDropPoint();
        if (dropPoint != null)
            return dropPoint.position + teleportGroundOffset;

        Vector3 wanted = transform.position - transform.forward * fallbackTeleportDistance;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(wanted, out hit, teleportNavMeshSampleRadius, NavMesh.AllAreas))
            return hit.position + teleportGroundOffset;

        return wanted + teleportGroundOffset;
    }

    Transform ChooseDropPoint()
    {
        if (teleportDropPoints == null || teleportDropPoints.Length == 0)
            return null;

        int start = Random.Range(0, teleportDropPoints.Length);
        for (int i = 0; i < teleportDropPoints.Length; i++)
        {
            Transform point = teleportDropPoints[(start + i) % teleportDropPoints.Length];
            if (point != null)
                return point;
        }

        return null;
    }

    void TeleportTransform(Transform target, Vector3 position, Quaternion rotation)
    {
        if (target == null) return;

        Rigidbody body = target.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            return;
        }

        target.SetPositionAndRotation(position, rotation);
    }

    void Reveal()
    {
        ChangeState(GhostWaterState.Revealed);
        revealedTimer = 0f;
        redisguiseTimer = redisguiseDelay;

        if (revealEffectPlayed) return;
        revealEffectPlayed = true;

        EnemyPlayerEffects.Pulse(
            ref effectTargetMovement,
            targetStatus,
            targetPlayer,
            cameraEffects,
            revealedVignette,
            revealShakeDuration,
            revealShakeAmplitude,
            revealShakeFrequency,
            revealShakeDuration);
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
            rainVisualRoot.SetActive(!hideRainWhileDisguised || !disguised);
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindAnyObjectByType<PlayerVignetteEffect>();
    }

    void UpdateCameraEffect(float distanceToPlayer)
    {
        if (state == GhostWaterState.Disguised)
        {
            EnemyPlayerEffects.ClearThreat(ref effectTargetMovement, cameraEffects);
            return;
        }

        float range = Mathf.Max(0.01f, chaseRange);
        float danger = 1f - Mathf.Clamp01(distanceToPlayer / range);
        float maxIntensity = state == GhostWaterState.Swallowing ? swallowingVignette : revealedVignette;
        PlayerStatus effectStatus = state == GhostWaterState.Swallowing
            ? swallowedStatus
            : targetStatus;
        Transform effectPlayer = state == GhostWaterState.Swallowing
            ? swallowedPlayer
            : targetPlayer;
        EnemyPlayerEffects.SetThreatIntensity(
            ref effectTargetMovement,
            effectStatus,
            effectPlayer,
            cameraEffects,
            maxIntensity * danger);
    }

    void OnDestroy()
    {
        RestoreVictimControls(swallowedStatus);
        EnemyPlayerEffects.ClearThreat(ref effectTargetMovement, cameraEffects);
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
