using UnityEngine;
using UnityEngine.AI;

public class BathroomBlondeBehavior : MonoBehaviour
{
    enum BathroomBlondeState
    {
        Dormant,
        Haunting,
        Manifesting,
        Chasing,
        Grabbing,
        Retreating
    }

    [Header("Visual State")]
    public GameObject dormantVisualRoot;
    public GameObject hauntingVisualRoot;
    public GameObject manifestedVisualRoot;
    public Transform bodyRoot;
    public float dormantLocalY = -1.4f;
    public float manifestedLocalY = 0f;
    public float riseSpeed = 3f;

    [Header("Haunting")]
    public Transform[] hauntPoints;
    public bool useWaterSourcesAsHauntPoints = true;
    public float awakenRange = 14f;
    public float waterSourceAwakenRange = 7f;
    public float hauntDuration = 2.5f;
    public float manifestDelay = 1.25f;
    public float dormantCooldown = 8f;
    public float teleportSampleRadius = 5f;
    public bool teleportToHauntPoint = true;

    [Header("Movement")]
    public float chaseSpeed = 4.75f;
    public float retreatSpeed = 6f;
    public float wanderSpeed = 1.75f;
    public float wanderRadius = 8f;
    public float destinationReachedDistance = 1.25f;
    public float rotationSpeed = 8f;
    public float loseInterestDistance = 22f;

    [Header("Attack")]
    public float grabRange = 1.6f;
    public float grabDuration = 3f;
    public float grabDamagePerSecond = 22f;
    public Transform grabHoldPoint;
    public Vector3 grabHoldOffset = new Vector3(0f, 1.15f, 0.55f);
    public bool blockPlayerControlsWhileGrabbed = true;
    public bool contaminateVictimWater = true;
    public float contaminateVictimInterval = 0.75f;

    [Header("Water Reaction")]
    public float cleanWaterRepelRequired = 35f;
    public float particleWaterRepelAmount = 8f;
    public bool chemicalWaterRepels = true;
    public bool contaminatedWaterAngers = true;
    public float angerSpeedMultiplier = 1.35f;
    public float angerDuration = 5f;

    [Header("Retreat")]
    public float retreatDistance = 10f;
    public float retreatDuration = 3f;
    public int retreatSampleCount = 8;
    public float retreatSampleAngle = 160f;

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float hauntingVignette = 0.2f;
    [Range(0f, 1f)] public float chaseVignette = 0.55f;
    [Range(0f, 1f)] public float grabVignette = 0.85f;
    public float manifestShakeAmplitude = 0.65f;
    public float manifestShakeFrequency = 9f;
    public float manifestShakeDuration = 0.35f;
    public float grabShakeAmplitude = 1f;
    public float grabShakeFrequency = 13f;
    public float grabShakeDuration = 0.35f;

    private NavMeshAgent agent;
    private BathroomBlondeState state = BathroomBlondeState.Dormant;
    private Transform targetPlayer;
    private PlayerStatus targetStatus;
    private float stateTimer;
    private float cooldownTimer;
    private float grabTimer;
    private float contaminationTimer;
    private float repelProgress;
    private float angerTimer;
    private Vector3 retreatTarget;
    private PlayerMovement grabbedMovement;
    private PlayerInventory grabbedInventory;
    private WaterCannon grabbedWaterCannon;
    private Rigidbody grabbedRigidbody;
    private bool grabbedMovementWasEnabled;
    private bool grabbedInventoryWasEnabled;
    private bool grabbedWaterCannonWasEnabled;
    private bool grabbedRigidbodyWasKinematic;
    private bool grabbedRigidbodyUsedGravity;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        ResolveCameraEffects();
        EnterDormant(true);
    }

    void Update()
    {
        ResolveCameraEffects();

        if (state != BathroomBlondeState.Grabbing)
            ResolveTarget();

        UpdateAnger();

        switch (state)
        {
            case BathroomBlondeState.Dormant:
                UpdateDormant();
                break;
            case BathroomBlondeState.Haunting:
                UpdateHaunting();
                break;
            case BathroomBlondeState.Manifesting:
                UpdateManifesting();
                break;
            case BathroomBlondeState.Chasing:
                UpdateChasing();
                break;
            case BathroomBlondeState.Grabbing:
                UpdateGrabbing();
                break;
            case BathroomBlondeState.Retreating:
                UpdateRetreating();
                break;
        }
    }

    void ResolveTarget()
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

    void UpdateDormant()
    {
        StopAgent();
        MoveBodyToward(dormantLocalY, riseSpeed);
        ClearCameraEffect();

        cooldownTimer -= Time.deltaTime;
        if (cooldownTimer > 0f || targetPlayer == null)
            return;

        if (ShouldAwakenForTarget())
            BeginHaunting();
    }

    bool ShouldAwakenForTarget()
    {
        if (targetPlayer == null || targetStatus == null) return false;
        if (Vector3.Distance(transform.position, targetPlayer.position) <= awakenRange)
            return true;

        if (targetStatus.HasContaminatedWater())
            return true;

        return IsPlayerNearWaterSource(targetPlayer.position);
    }

    bool IsPlayerNearWaterSource(Vector3 position)
    {
        if (!useWaterSourcesAsHauntPoints) return false;

        WaterSourceDryable[] sources = FindObjectsOfType<WaterSourceDryable>();
        for (int i = 0; i < sources.Length; i++)
        {
            WaterSourceDryable source = sources[i];
            if (source == null || source.isDry) continue;
            if (Vector3.Distance(position, source.transform.position) <= waterSourceAwakenRange)
                return true;
        }

        return false;
    }

    void BeginHaunting()
    {
        state = BathroomBlondeState.Haunting;
        stateTimer = hauntDuration;
        repelProgress = 0f;

        if (teleportToHauntPoint)
            TeleportToHauntPosition();

        StopAgent();
        UpdateVisuals();
    }

    void UpdateHaunting()
    {
        StopAgent();
        MoveBodyToward(dormantLocalY, riseSpeed);
        UpdateCameraEffect(hauntingVignette);

        stateTimer -= Time.deltaTime;
        if (stateTimer <= hauntDuration - manifestDelay)
            BeginManifesting();
    }

    void BeginManifesting()
    {
        state = BathroomBlondeState.Manifesting;
        stateTimer = manifestDelay;
        StopAgent();

        if (cameraEffects != null)
        {
            cameraEffects.Pulse(chaseVignette, manifestShakeDuration);
            cameraEffects.Shake(manifestShakeAmplitude, manifestShakeFrequency, manifestShakeDuration);
        }

        UpdateVisuals();
    }

    void UpdateManifesting()
    {
        StopAgent();
        MoveBodyToward(manifestedLocalY, riseSpeed);
        if (targetPlayer != null)
            FaceTarget(targetPlayer.position);

        UpdateCameraEffect(chaseVignette);
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
            BeginChasing();
    }

    void BeginChasing()
    {
        state = BathroomBlondeState.Chasing;
        SetAgentStopped(false);
        UpdateVisuals();
    }

    void UpdateChasing()
    {
        MoveBodyToward(manifestedLocalY, riseSpeed);

        if (targetPlayer == null || targetStatus == null || targetStatus.IsDead())
        {
            Wander();
            UpdateCameraEffect(0f);
            return;
        }

        float distance = Vector3.Distance(transform.position, targetPlayer.position);
        UpdateChaseCameraEffect(distance);

        if (distance > loseInterestDistance)
        {
            BeginRetreat();
            return;
        }

        float speed = angerTimer > 0f ? chaseSpeed * angerSpeedMultiplier : chaseSpeed;
        MoveTo(targetPlayer.position, speed);

        if (distance <= grabRange)
            BeginGrab(targetStatus);
    }

    void BeginGrab(PlayerStatus victim)
    {
        if (victim == null || victim.IsDead()) return;

        state = BathroomBlondeState.Grabbing;
        targetStatus = victim;
        targetPlayer = victim.transform;
        grabTimer = grabDuration;
        contaminationTimer = 0f;
        StoreAndBlockVictimControls(victim);
        StopAgent();

        if (cameraEffects != null)
        {
            cameraEffects.Pulse(grabVignette, grabShakeDuration);
            cameraEffects.Shake(grabShakeAmplitude, grabShakeFrequency, grabShakeDuration);
        }

        UpdateVisuals();
    }

    void UpdateGrabbing()
    {
        if (targetPlayer == null || targetStatus == null || targetStatus.IsDead())
        {
            BeginRetreat();
            return;
        }

        HoldGrabbedPlayer();
        FaceTarget(targetPlayer.position);
        UpdateCameraEffect(grabVignette);

        grabTimer -= Time.deltaTime;
        targetStatus.TakeDamage(grabDamagePerSecond * Time.deltaTime);

        contaminationTimer -= Time.deltaTime;
        if (contaminateVictimWater && contaminationTimer <= 0f)
        {
            contaminationTimer = contaminateVictimInterval;
            targetStatus.ContaminateWater();
        }

        if (grabTimer <= 0f || targetStatus.IsDead())
            BeginRetreat();
    }

    void BeginRetreat()
    {
        RestoreVictimControls(targetStatus);
        ClearGrabbedReferences();
        retreatTarget = ChooseRetreatTarget();
        state = BathroomBlondeState.Retreating;
        stateTimer = retreatDuration;
        SetAgentStopped(false);
        UpdateVisuals();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.speed = retreatSpeed;
            agent.SetDestination(retreatTarget);
        }
    }

    void UpdateRetreating()
    {
        MoveBodyToward(manifestedLocalY, riseSpeed);
        MoveTo(retreatTarget, retreatSpeed);
        UpdateCameraEffect(hauntingVignette);

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f || Vector3.Distance(transform.position, retreatTarget) <= destinationReachedDistance)
            EnterDormant(false);
    }

    void EnterDormant(bool immediate)
    {
        RestoreVictimControls(targetStatus);
        ClearGrabbedReferences();
        state = BathroomBlondeState.Dormant;
        stateTimer = 0f;
        grabTimer = 0f;
        repelProgress = 0f;
        angerTimer = 0f;
        cooldownTimer = dormantCooldown;
        StopAgent();

        if (immediate)
            SetBodyLocalY(dormantLocalY);

        UpdateVisuals();
        ClearCameraEffect();
    }

    public void ApplyWater(WaterQuality quality, float amount)
    {
        if (amount <= 0f || state == BathroomBlondeState.Dormant)
            return;

        if (quality == WaterQuality.Clean || (chemicalWaterRepels && quality == WaterQuality.ChemicallyEnhanced))
        {
            repelProgress += amount;
            if (repelProgress >= cleanWaterRepelRequired)
                BeginRetreat();
            return;
        }

        if (quality == WaterQuality.Contaminated && contaminatedWaterAngers)
            angerTimer = angerDuration;
    }

    public void ReceiveWaterHit(Vector3 sourcePosition)
    {
        if (state == BathroomBlondeState.Dormant)
            return;

        repelProgress += particleWaterRepelAmount;
        if (repelProgress >= cleanWaterRepelRequired)
            BeginRetreat();
    }

    void TeleportToHauntPosition()
    {
        Vector3 position = ChooseHauntPosition();
        TeleportTransform(transform, position, transform.rotation);
    }

    Vector3 ChooseHauntPosition()
    {
        Transform explicitPoint = ChooseExplicitHauntPoint();
        if (explicitPoint != null)
            return explicitPoint.position;

        Vector3 waterPoint;
        if (TryFindWaterSourceHauntPosition(out waterPoint))
            return waterPoint;

        if (targetPlayer != null)
        {
            Vector3 behindPlayer = targetPlayer.position - targetPlayer.forward * 3f;
            Vector3 sampled;
            if (TrySampleNavMesh(behindPlayer, teleportSampleRadius, out sampled))
                return sampled;
        }

        return transform.position;
    }

    Transform ChooseExplicitHauntPoint()
    {
        if (hauntPoints == null || hauntPoints.Length == 0)
            return null;

        int start = Random.Range(0, hauntPoints.Length);
        for (int i = 0; i < hauntPoints.Length; i++)
        {
            Transform point = hauntPoints[(start + i) % hauntPoints.Length];
            if (point != null)
                return point;
        }

        return null;
    }

    bool TryFindWaterSourceHauntPosition(out Vector3 position)
    {
        position = transform.position;
        if (!useWaterSourcesAsHauntPoints)
            return false;

        WaterSourceDryable[] sources = FindObjectsOfType<WaterSourceDryable>();
        float bestScore = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < sources.Length; i++)
        {
            WaterSourceDryable source = sources[i];
            if (source == null || source.isDry) continue;

            Vector3 candidate = source.transform.position;
            float score = targetPlayer != null
                ? Vector3.SqrMagnitude(candidate - targetPlayer.position)
                : Vector3.SqrMagnitude(candidate - transform.position);

            if (score > waterSourceAwakenRange * waterSourceAwakenRange && targetPlayer != null)
                continue;

            Vector3 sampled;
            if (!TrySampleNavMesh(candidate, teleportSampleRadius, out sampled))
                continue;

            if (score >= bestScore) continue;

            bestScore = score;
            position = sampled;
            found = true;
        }

        return found;
    }

    Vector3 ChooseRetreatTarget()
    {
        Vector3 away = targetPlayer != null
            ? transform.position - targetPlayer.position
            : -transform.forward;
        away.y = 0f;

        if (away.sqrMagnitude <= 0.001f)
            away = -transform.forward;

        return ChooseReachablePointInDirection(away.normalized, retreatDistance);
    }

    Vector3 ChooseReachablePointInDirection(Vector3 direction, float distance)
    {
        int samples = Mathf.Max(1, retreatSampleCount);
        float angleRange = Mathf.Max(0f, retreatSampleAngle);
        Vector3 best = transform.position + direction * distance;
        float bestScore = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i < samples; i++)
        {
            float t = samples == 1 ? 0f : (i / (samples - 1f)) - 0.5f;
            Vector3 sampleDirection = Quaternion.Euler(0f, t * angleRange, 0f) * direction;
            Vector3 wanted = transform.position + sampleDirection.normalized * distance;

            Vector3 sampled;
            if (!TrySampleReachablePoint(wanted, distance, out sampled))
                continue;

            float score = targetPlayer != null
                ? Vector3.SqrMagnitude(sampled - targetPlayer.position)
                : Vector3.SqrMagnitude(sampled - transform.position);

            if (score <= bestScore) continue;

            bestScore = score;
            best = sampled;
            found = true;
        }

        if (found)
            return best;

        Vector3 fallback;
        if (TrySampleNavMesh(best, distance, out fallback))
            return fallback;

        return best;
    }

    bool TrySampleReachablePoint(Vector3 wanted, float sampleRadius, out Vector3 sampled)
    {
        if (!TrySampleNavMesh(wanted, sampleRadius, out sampled))
            return false;

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return true;

        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(sampled, path))
            return false;

        return path.status == NavMeshPathStatus.PathComplete;
    }

    bool TrySampleNavMesh(Vector3 wanted, float sampleRadius, out Vector3 sampled)
    {
        sampled = wanted;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(wanted, out hit, Mathf.Max(0.1f, sampleRadius), NavMesh.AllAreas))
            return false;

        sampled = hit.position;
        return true;
    }

    void Wander()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        agent.speed = wanderSpeed;
        if (!agent.pathPending && agent.remainingDistance <= destinationReachedDistance)
            SetWanderDestination();
    }

    void SetWanderDestination()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius + transform.position;
        Vector3 sampled;
        if (!TrySampleNavMesh(randomDirection, wanderRadius, out sampled)) return;

        agent.isStopped = false;
        agent.speed = wanderSpeed;
        agent.SetDestination(sampled);
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

    void StopAgent()
    {
        SetAgentStopped(true);
    }

    void SetAgentStopped(bool stopped)
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = stopped;
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    void StoreAndBlockVictimControls(PlayerStatus victim)
    {
        if (!blockPlayerControlsWhileGrabbed || victim == null) return;

        grabbedMovement = victim.GetComponent<PlayerMovement>();
        grabbedInventory = victim.GetComponent<PlayerInventory>();
        grabbedWaterCannon = victim.GetComponentInChildren<WaterCannon>();
        grabbedRigidbody = victim.GetComponent<Rigidbody>();

        if (grabbedMovement != null)
        {
            grabbedMovementWasEnabled = grabbedMovement.enabled;
            grabbedMovement.enabled = false;
        }

        if (grabbedInventory != null)
        {
            grabbedInventoryWasEnabled = grabbedInventory.enabled;
            grabbedInventory.enabled = false;
        }

        if (grabbedWaterCannon != null)
        {
            grabbedWaterCannonWasEnabled = grabbedWaterCannon.enabled;
            grabbedWaterCannon.enabled = false;
        }

        if (grabbedRigidbody != null)
        {
            grabbedRigidbodyWasKinematic = grabbedRigidbody.isKinematic;
            grabbedRigidbodyUsedGravity = grabbedRigidbody.useGravity;
            grabbedRigidbody.linearVelocity = Vector3.zero;
            grabbedRigidbody.angularVelocity = Vector3.zero;
            grabbedRigidbody.useGravity = false;
            grabbedRigidbody.isKinematic = true;
        }
    }

    void RestoreVictimControls(PlayerStatus victim)
    {
        if (!blockPlayerControlsWhileGrabbed) return;
        bool canRestore = victim != null && victim.CanAct();

        if (grabbedMovement != null)
            grabbedMovement.enabled = canRestore && grabbedMovementWasEnabled;

        if (grabbedInventory != null)
            grabbedInventory.enabled = canRestore && grabbedInventoryWasEnabled;

        if (grabbedWaterCannon != null)
            grabbedWaterCannon.enabled = canRestore && grabbedWaterCannonWasEnabled;

        if (grabbedRigidbody != null && canRestore)
        {
            grabbedRigidbody.isKinematic = grabbedRigidbodyWasKinematic;
            grabbedRigidbody.useGravity = grabbedRigidbodyUsedGravity;
        }
    }

    void ClearGrabbedReferences()
    {
        grabbedMovement = null;
        grabbedInventory = null;
        grabbedWaterCannon = null;
        grabbedRigidbody = null;
        grabbedMovementWasEnabled = false;
        grabbedInventoryWasEnabled = false;
        grabbedWaterCannonWasEnabled = false;
    }

    void HoldGrabbedPlayer()
    {
        if (targetPlayer == null) return;

        Vector3 holdPosition = grabHoldPoint != null
            ? grabHoldPoint.position
            : transform.TransformPoint(grabHoldOffset);

        TeleportTransform(targetPlayer, holdPosition, transform.rotation);
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

    void MoveBodyToward(float targetY, float speed)
    {
        if (bodyRoot == null) return;

        Vector3 local = bodyRoot.localPosition;
        local.y = Mathf.MoveTowards(local.y, targetY, speed * Time.deltaTime);
        bodyRoot.localPosition = local;
    }

    void SetBodyLocalY(float y)
    {
        if (bodyRoot == null) return;

        Vector3 local = bodyRoot.localPosition;
        local.y = y;
        bodyRoot.localPosition = local;
    }

    void UpdateVisuals()
    {
        bool dormant = state == BathroomBlondeState.Dormant;
        bool haunting = state == BathroomBlondeState.Haunting;
        bool manifested = state == BathroomBlondeState.Manifesting ||
            state == BathroomBlondeState.Chasing ||
            state == BathroomBlondeState.Grabbing ||
            state == BathroomBlondeState.Retreating;

        if (dormantVisualRoot != null)
            dormantVisualRoot.SetActive(dormant);

        if (hauntingVisualRoot != null)
            hauntingVisualRoot.SetActive(haunting);

        if (manifestedVisualRoot != null)
            manifestedVisualRoot.SetActive(manifested);
    }

    void UpdateAnger()
    {
        if (angerTimer <= 0f) return;
        angerTimer -= Time.deltaTime;
        if (angerTimer < 0f)
            angerTimer = 0f;
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindObjectOfType<PlayerVignetteEffect>();
    }

    void UpdateChaseCameraEffect(float distance)
    {
        if (cameraEffects == null) return;

        float range = Mathf.Max(0.01f, loseInterestDistance);
        float danger = 1f - Mathf.Clamp01(distance / range);
        cameraEffects.SetThreatIntensity(chaseVignette * danger);
    }

    void UpdateCameraEffect(float intensity)
    {
        if (cameraEffects != null)
            cameraEffects.SetThreatIntensity(intensity);
    }

    void ClearCameraEffect()
    {
        if (cameraEffects != null)
        {
            cameraEffects.ClearThreatIntensity();
            cameraEffects.StopShake();
        }
    }

    void OnDestroy()
    {
        RestoreVictimControls(targetStatus);
        ClearCameraEffect();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, awakenRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, waterSourceAwakenRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, grabRange);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(retreatTarget, destinationReachedDistance);
    }
}
