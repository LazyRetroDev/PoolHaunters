using UnityEngine;
using UnityEngine.AI;

public class RaccoonBehavior : MonoBehaviour
{
    enum RaccoonState
    {
        Wandering,
        InvestigatingNoise,
        StalkingPlayer,
        FleeingWithItem,
        EatingItem,
        Harassing
    }

    [Header("Movement")]
    public float wanderSpeed = 2.2f;
    public float stalkSpeed = 3.8f;
    public float fleeSpeed = 6f;
    public float rotationSpeed = 8f;
    public float wanderRadius = 12f;
    public float destinationReachedDistance = 1.25f;

    [Header("Detection")]
    public float sightRange = 9f;
    [Range(1f, 180f)] public float fieldOfView = 115f;
    public float stealRange = 1.6f;
    public float harassRange = 1.4f;
    public LayerMask lineOfSightMask = ~0;
    public Transform player;

    [Header("Noise Attraction")]
    public bool reactsToNoise = true;
    public float hearingMultiplier = 1.15f;
    public float noiseMemoryDuration = 5f;

    [Header("Stealing")]
    public bool stealItems = true;
    public Transform carryPoint;
    public Vector3 carryOffset = new Vector3(0f, 0.55f, 0.45f);
    public float eatItemDuration = 4f;
    public float fleeDistanceAfterSteal = 8f;
    public bool destroyItemAfterEating = false;
    public GameObject droppedItemMarkerPrefab;

    [Header("Harass Attack")]
    public float damage = 5f;
    public float attackCooldown = 1.1f;
    public float waterContaminationChance = 0.35f;

    [Header("Contamination")]
    public GameObject contaminationTrailPrefab;
    public float contaminationTrailInterval = 1.25f;
    public bool contaminateWaterOnHit = true;

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float nearbyVignette = 0.18f;
    public float stealShakeAmplitude = 0.45f;
    public float stealShakeFrequency = 12f;
    public float stealShakeDuration = 0.25f;

    private NavMeshAgent agent;
    private PlayerStatus playerStatus;
    private PlayerInventory playerInventory;
    private Item carriedItem;
    private Vector3 lastNoisePosition;
    private Vector3 fleeTarget;
    private float noiseMemoryTimer;
    private float eatTimer;
    private float nextAttackTime;
    private float trailTimer;
    private RaccoonState state = RaccoonState.Wandering;

    void OnEnable()
    {
        if (reactsToNoise)
            NoiseEvent.OnNoiseEmitted += OnNoiseHeard;
    }

    void OnDisable()
    {
        if (reactsToNoise)
            NoiseEvent.OnNoiseEmitted -= OnNoiseHeard;

        ClearCameraEffect();
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        ResolvePlayer();
        ResolveCameraEffects();
        SetupAgent(wanderSpeed);
        SetWanderDestination();
    }

    void Update()
    {
        ResolvePlayer();
        ResolveCameraEffects();
        UpdateCarriedItem();
        UpdateNoiseMemory();
        UpdateState();
        UpdateCameraEffect();
        TryLeaveContaminationTrail();
    }

    void ResolvePlayer()
    {
        if (player != null && playerStatus != null && playerInventory != null) return;

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                player = playerObject.transform;
        }

        if (player == null) return;

        if (playerStatus == null)
            playerStatus = player.GetComponent<PlayerStatus>();

        if (playerInventory == null)
            playerInventory = player.GetComponent<PlayerInventory>();
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindObjectOfType<PlayerVignetteEffect>();
    }

    void SetupAgent(float speed)
    {
        if (agent == null) return;

        agent.speed = speed;
        agent.angularSpeed = 360f;
        agent.stoppingDistance = destinationReachedDistance;
    }

    void UpdateState()
    {
        switch (state)
        {
            case RaccoonState.Wandering:
                UpdateWandering();
                break;
            case RaccoonState.InvestigatingNoise:
                UpdateInvestigatingNoise();
                break;
            case RaccoonState.StalkingPlayer:
                UpdateStalkingPlayer();
                break;
            case RaccoonState.FleeingWithItem:
                UpdateFleeingWithItem();
                break;
            case RaccoonState.EatingItem:
                UpdateEatingItem();
                break;
            case RaccoonState.Harassing:
                UpdateHarassing();
                break;
        }
    }

    void UpdateWandering()
    {
        if (CanSeePlayer())
        {
            ChangeState(RaccoonState.StalkingPlayer);
            return;
        }

        if (noiseMemoryTimer > 0f)
        {
            ChangeState(RaccoonState.InvestigatingNoise);
            return;
        }

        Wander();
    }

    void UpdateInvestigatingNoise()
    {
        if (CanSeePlayer())
        {
            ChangeState(RaccoonState.StalkingPlayer);
            return;
        }

        MoveTo(lastNoisePosition, stalkSpeed);
        if (ReachedDestination(lastNoisePosition) || noiseMemoryTimer <= 0f)
            ChangeState(RaccoonState.Wandering);
    }

    void UpdateStalkingPlayer()
    {
        if (player == null || playerStatus == null || playerStatus.IsDead())
        {
            ChangeState(RaccoonState.Wandering);
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);
        MoveTo(player.position, stalkSpeed);

        if (stealItems && distance <= stealRange && TryStealItem())
        {
            BeginFleeWithItem();
            return;
        }

        if (distance <= harassRange)
            ChangeState(RaccoonState.Harassing);
    }

    void UpdateFleeingWithItem()
    {
        MoveTo(fleeTarget, fleeSpeed);
        if (ReachedDestination(fleeTarget))
        {
            eatTimer = eatItemDuration;
            ChangeState(RaccoonState.EatingItem);
        }
    }

    void UpdateEatingItem()
    {
        StopAgent();
        eatTimer -= Time.deltaTime;

        if (eatTimer > 0f) return;

        DropOrDestroyCarriedItem();
        ChangeState(RaccoonState.Wandering);
    }

    void UpdateHarassing()
    {
        if (player == null || playerStatus == null || playerStatus.IsDead())
        {
            ChangeState(RaccoonState.Wandering);
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);
        FaceTarget(player.position);

        if (distance > harassRange * 1.5f)
        {
            ChangeState(RaccoonState.StalkingPlayer);
            return;
        }

        StopAgent();
        TryAttackPlayer();
    }

    void ChangeState(RaccoonState newState)
    {
        state = newState;

        switch (state)
        {
            case RaccoonState.Wandering:
                SetupAgent(wanderSpeed);
                SetWanderDestination();
                break;
            case RaccoonState.InvestigatingNoise:
            case RaccoonState.StalkingPlayer:
                SetupAgent(stalkSpeed);
                break;
            case RaccoonState.FleeingWithItem:
                SetupAgent(fleeSpeed);
                break;
        }
    }

    void Wander()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        agent.speed = wanderSpeed;
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            SetWanderDestination();
    }

    void SetWanderDestination()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius + transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
        {
            agent.isStopped = false;
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

    void StopAgent()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = true;
    }

    bool ReachedDestination(Vector3 target)
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            return !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;

        return Vector3.Distance(transform.position, target) <= destinationReachedDistance;
    }

    bool CanSeePlayer()
    {
        if (player == null || playerStatus == null || !playerStatus.CanAct()) return false;

        Vector3 eyePosition = transform.position + Vector3.up * 0.6f;
        Vector3 directionToPlayer = player.position - eyePosition;
        float distanceToPlayer = directionToPlayer.magnitude;

        if (distanceToPlayer > sightRange) return false;

        float angle = Vector3.Angle(transform.forward, directionToPlayer);
        if (angle > fieldOfView * 0.5f) return false;

        if (Physics.Raycast(eyePosition, directionToPlayer.normalized, out RaycastHit hit, sightRange, lineOfSightMask, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<PlayerStatus>() == playerStatus;

        return false;
    }

    bool TryStealItem()
    {
        if (playerInventory == null) return false;

        Item[] slots = playerInventory.GetSlots();
        if (slots == null || slots.Length == 0) return false;

        int startIndex = Mathf.Clamp(playerInventory.GetSelectedSlot(), 0, slots.Length - 1);
        for (int i = 0; i < slots.Length; i++)
        {
            int slotIndex = (startIndex + i) % slots.Length;
            Item item = slots[slotIndex];
            if (item == null) continue;

            if (!playerInventory.RemoveItem(item, destroyItem: false)) continue;

            carriedItem = item;
            AttachCarriedItem();
            ShakeOnSteal();
            return true;
        }

        return false;
    }

    void AttachCarriedItem()
    {
        if (carriedItem == null) return;

        carriedItem.gameObject.SetActive(true);
        Transform parent = carryPoint != null ? carryPoint : transform;
        carriedItem.transform.SetParent(parent, false);
        carriedItem.transform.localPosition = carryPoint != null ? Vector3.zero : carryOffset;
        carriedItem.transform.localRotation = Quaternion.identity;

        Collider[] colliders = carriedItem.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;
    }

    void BeginFleeWithItem()
    {
        Vector3 awayFromPlayer = player != null ? (transform.position - player.position).normalized : -transform.forward;
        if (awayFromPlayer.sqrMagnitude <= 0.001f)
            awayFromPlayer = -transform.forward;

        Vector3 wantedTarget = transform.position + awayFromPlayer * fleeDistanceAfterSteal;
        fleeTarget = wantedTarget;

        if (NavMesh.SamplePosition(wantedTarget, out NavMeshHit hit, fleeDistanceAfterSteal, NavMesh.AllAreas))
            fleeTarget = hit.position;

        ChangeState(RaccoonState.FleeingWithItem);
    }

    void DropOrDestroyCarriedItem()
    {
        if (carriedItem == null) return;

        if (destroyItemAfterEating)
        {
            Destroy(carriedItem.gameObject);
            carriedItem = null;
            return;
        }

        carriedItem.transform.SetParent(null, true);
        carriedItem.transform.position = transform.position + transform.forward * 0.75f + Vector3.up * 0.15f;
        carriedItem.gameObject.SetActive(true);

        Collider[] colliders = carriedItem.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = true;

        if (droppedItemMarkerPrefab != null)
            Instantiate(droppedItemMarkerPrefab, carriedItem.transform.position, Quaternion.identity);

        carriedItem = null;
    }

    void UpdateCarriedItem()
    {
        if (carriedItem == null) return;

        Transform parent = carryPoint != null ? carryPoint : transform;
        if (carriedItem.transform.parent != parent)
            carriedItem.transform.SetParent(parent, false);
    }

    void TryAttackPlayer()
    {
        if (Time.time < nextAttackTime) return;

        nextAttackTime = Time.time + attackCooldown;
        playerStatus.TakeDamage(damage);

        if (contaminateWaterOnHit && Random.value <= waterContaminationChance)
            playerStatus.ContaminateWater();
    }

    void TryLeaveContaminationTrail()
    {
        if (contaminationTrailPrefab == null) return;
        if (state != RaccoonState.FleeingWithItem && state != RaccoonState.Harassing) return;

        trailTimer -= Time.deltaTime;
        if (trailTimer > 0f) return;

        trailTimer = contaminationTrailInterval;
        Instantiate(contaminationTrailPrefab, transform.position, Quaternion.identity);
    }

    void OnNoiseHeard(Vector3 position, float radius, GameObject source)
    {
        if (source == gameObject) return;

        float hearingRadius = radius * hearingMultiplier;
        if (Vector3.Distance(transform.position, position) > hearingRadius) return;

        lastNoisePosition = position;
        noiseMemoryTimer = noiseMemoryDuration;

        if (state == RaccoonState.Wandering)
            ChangeState(RaccoonState.InvestigatingNoise);
    }

    void UpdateNoiseMemory()
    {
        if (noiseMemoryTimer <= 0f) return;

        noiseMemoryTimer -= Time.deltaTime;
        if (noiseMemoryTimer < 0f)
            noiseMemoryTimer = 0f;
    }

    void UpdateCameraEffect()
    {
        if (cameraEffects == null || player == null) return;

        bool threatening = state == RaccoonState.StalkingPlayer || state == RaccoonState.Harassing;
        if (!threatening)
        {
            ClearCameraEffect();
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);
        float intensity = Mathf.Lerp(nearbyVignette, 0f, Mathf.Clamp01(distance / sightRange));
        cameraEffects.SetThreatIntensity(intensity);
    }

    void ClearCameraEffect()
    {
        if (cameraEffects != null)
            cameraEffects.ClearThreatIntensity();
    }

    void ShakeOnSteal()
    {
        if (cameraEffects == null) return;

        cameraEffects.Pulse(nearbyVignette, stealShakeDuration);
        cameraEffects.Shake(stealShakeAmplitude, stealShakeFrequency, stealShakeDuration);
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    void OnDestroy()
    {
        ClearCameraEffect();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stealRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, harassRange);
    }
}
