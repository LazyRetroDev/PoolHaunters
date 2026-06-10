using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class GoldenMouthBehavior : MonoBehaviour
{
    enum GoldenMouthState
    {
        WaitingForHelp,
        Combusting,
        Aggressive,
        Pacified
    }

    [Header("Help / Fire State")]
    public float timeToExtinguish = 20f;
    public float extinguishRequired = 100f;
    public bool chemicalWaterCountsAsPure = false;
    public bool moveWhileWaitingForHelp = true;
    public float helpWanderSpeed = 1.75f;
    public GameObject fireVisual;
    public GameObject pacifiedVisual;

    [Header("Combustion")]
    public GameObject combustionVisual;
    public float combustionDuration = 1.5f;
    public float combustionDamageRadius = 4f;
    public float combustionDamage = 30f;
    public int burstHazardCount = 6;
    public float burstHazardRadius = 3f;

    [Header("Aggressive State")]
    public float chaseSpeed = 5.5f;
    public float wanderSpeed = 2.5f;
    public float wanderRadius = 12f;
    public float detectionRange = 12f;
    public float attackRange = 1.75f;
    public float damagePerAttack = 25f;
    public float attackCooldown = 1f;
    public float loseInterestDistance = 18f;

    [Header("After Kill")]
    public GameObject willOWispPrefab;
    public Vector3 willOWispSpawnOffset = new Vector3(0f, 1f, 0f);

    [Header("Fire Spread")]
    public GameObject fireHazardPrefab;
    public float fireHazardInterval = 1.5f;

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float combustionPulseIntensity = 0.9f;
    public float combustionPulseDuration = 0.6f;
    [Range(0f, 1f)] public float aggressiveMinVignette = 0.2f;
    [Range(0f, 1f)] public float aggressiveMaxVignette = 0.75f;
    public float combustionShakeAmplitude = 1.8f;
    public float combustionShakeFrequency = 15f;
    public float combustionShakeDuration = 0.8f;
    public float aggressiveMaxShakeAmplitude = 0.45f;
    public float aggressiveShakeFrequency = 7f;
    public float attackShakeAmplitude = 0.85f;
    public float attackShakeFrequency = 12f;
    public float attackShakeDuration = 0.25f;

    private readonly HashSet<PlayerStatus> transformedPlayers = new HashSet<PlayerStatus>();
    private NavMeshAgent agent;
    private Transform player;
    private PlayerStatus playerStatus;
    private GoldenMouthState state = GoldenMouthState.WaitingForHelp;
    private float extinguishProgress;
    private float helpTimer;
    private float combustionTimer;
    private float nextAttackTime;
    private float fireHazardTimer;
    private bool dealtCombustionDamage;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        helpTimer = timeToExtinguish;
        ResolvePlayer();
        ResolveCameraEffects();
        UpdateVisuals();
        SetWanderDestination(helpWanderSpeed);
    }

    void Update()
    {
        ResolvePlayer();
        ResolveCameraEffects();

        switch (state)
        {
            case GoldenMouthState.WaitingForHelp:
                UpdateHelpState();
                break;
            case GoldenMouthState.Combusting:
                UpdateCombustionState();
                break;
            case GoldenMouthState.Aggressive:
                UpdateAggressiveBehavior();
                TryLeaveFireHazard();
                break;
            case GoldenMouthState.Pacified:
                ClearCameraEffect();
                break;
        }
    }

    void ResolvePlayer()
    {
        if (player != null && playerStatus != null) return;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject == null) return;

        player = playerObject.transform;
        playerStatus = playerObject.GetComponent<PlayerStatus>();
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindObjectOfType<PlayerVignetteEffect>();
    }

    void UpdateHelpState()
    {
        helpTimer -= Time.deltaTime;

        if (moveWhileWaitingForHelp)
            Wander(helpWanderSpeed);
        else
            StopAgent();

        if (helpTimer <= 0f)
            StartCombustion();
    }

    public void ApplyWater(WaterQuality quality, float amount)
    {
        if (state != GoldenMouthState.WaitingForHelp || amount <= 0f) return;
        if (!IsPureWater(quality)) return;

        extinguishProgress += amount;
        if (extinguishProgress >= extinguishRequired)
            Pacify();
    }

    bool IsPureWater(WaterQuality quality)
    {
        if (quality == WaterQuality.Clean) return true;
        return chemicalWaterCountsAsPure && quality == WaterQuality.ChemicallyEnhanced;
    }

    void Pacify()
    {
        state = GoldenMouthState.Pacified;
        StopAgent();
        ClearCameraEffect();
        UpdateVisuals();
    }

    void StartCombustion()
    {
        state = GoldenMouthState.Combusting;
        combustionTimer = combustionDuration;
        dealtCombustionDamage = false;
        StopAgent();
        SpawnBurstFireHazards();

        if (cameraEffects != null)
        {
            cameraEffects.Pulse(combustionPulseIntensity, combustionPulseDuration);
            cameraEffects.Shake(combustionShakeAmplitude, combustionShakeFrequency, combustionShakeDuration);
        }

        UpdateVisuals();
    }

    void UpdateCombustionState()
    {
        FacePlayerIfVisible();

        if (!dealtCombustionDamage)
        {
            DealCombustionDamage();
            dealtCombustionDamage = true;
        }

        combustionTimer -= Time.deltaTime;
        if (combustionTimer <= 0f)
            BecomeAggressive();
    }

    void BecomeAggressive()
    {
        state = GoldenMouthState.Aggressive;
        fireHazardTimer = 0f;
        UpdateVisuals();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = chaseSpeed;
        }
    }

    void UpdateAggressiveBehavior()
    {
        if (player == null || playerStatus == null)
        {
            ClearCameraEffect();
            Wander(wanderSpeed);
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        UpdateAggressiveCameraEffect(distanceToPlayer);

        if (distanceToPlayer <= detectionRange)
        {
            MoveTo(player.position, chaseSpeed);
            TryAttack(distanceToPlayer);
            return;
        }

        if (distanceToPlayer >= loseInterestDistance)
        {
            ClearCameraEffect();
            Wander(wanderSpeed);
        }
        else
        {
            MoveTo(player.position, wanderSpeed);
        }
    }

    void UpdateAggressiveCameraEffect(float distanceToPlayer)
    {
        if (cameraEffects == null) return;

        if (distanceToPlayer > loseInterestDistance)
        {
            cameraEffects.ClearThreatIntensity();
            cameraEffects.StopShake();
            return;
        }

        float danger = 1f - Mathf.Clamp01(distanceToPlayer / Mathf.Max(0.01f, detectionRange));
        cameraEffects.SetThreatIntensity(Mathf.Lerp(aggressiveMinVignette, aggressiveMaxVignette, danger));
        cameraEffects.Shake(aggressiveMaxShakeAmplitude * danger, aggressiveShakeFrequency, 0.15f);
    }

    void TryAttack(float distanceToPlayer)
    {
        if (distanceToPlayer > attackRange || Time.time < nextAttackTime) return;

        if (cameraEffects != null)
            cameraEffects.Shake(attackShakeAmplitude, attackShakeFrequency, attackShakeDuration);

        bool killed = playerStatus.TakeDamage(damagePerAttack);
        nextAttackTime = Time.time + attackCooldown;

        if (killed)
            HandlePlayerKilled(playerStatus);
    }

    void HandlePlayerKilled(PlayerStatus killedPlayer)
    {
        if (killedPlayer == null || transformedPlayers.Contains(killedPlayer)) return;
        transformedPlayers.Add(killedPlayer);

        if (willOWispPrefab != null)
            Instantiate(willOWispPrefab, killedPlayer.transform.position + willOWispSpawnOffset, Quaternion.identity);

        killedPlayer.ApplyDeathTransformation();
    }

    void Wander(float speed)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        agent.speed = speed;
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            SetWanderDestination(speed);
    }

    void SetWanderDestination(float speed)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius + transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
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

    void StopAgent()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = true;
    }

    void FacePlayerIfVisible()
    {
        if (player != null)
            FaceTarget(player.position);
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f) return;

        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction.normalized), Time.deltaTime * 8f);
    }

    void DealCombustionDamage()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, combustionDamageRadius);
        for (int i = 0; i < hits.Length; i++)
        {
            PlayerStatus status = hits[i].GetComponentInParent<PlayerStatus>();
            if (status == null) continue;

            bool killed = status.TakeDamage(combustionDamage);
            if (killed)
                HandlePlayerKilled(status);
        }
    }

    void TryLeaveFireHazard()
    {
        if (fireHazardPrefab == null) return;

        fireHazardTimer -= Time.deltaTime;
        if (fireHazardTimer > 0f) return;

        fireHazardTimer = fireHazardInterval;
        Instantiate(fireHazardPrefab, transform.position, Quaternion.identity);
    }

    void SpawnBurstFireHazards()
    {
        if (fireHazardPrefab == null) return;

        Instantiate(fireHazardPrefab, transform.position, Quaternion.identity);

        int count = Mathf.Max(0, burstHazardCount);
        for (int i = 0; i < count; i++)
        {
            Vector2 offset2D = Random.insideUnitCircle * burstHazardRadius;
            Vector3 spawnPosition = transform.position + new Vector3(offset2D.x, 0f, offset2D.y);
            if (NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, burstHazardRadius, NavMesh.AllAreas))
                spawnPosition = hit.position;

            Instantiate(fireHazardPrefab, spawnPosition, Quaternion.identity);
        }
    }

    void ClearCameraEffect()
    {
        if (cameraEffects != null)
        {
            cameraEffects.ClearThreatIntensity();
            cameraEffects.StopShake();
        }
    }

    void UpdateVisuals()
    {
        bool pacified = state == GoldenMouthState.Pacified;
        bool combusting = state == GoldenMouthState.Combusting;

        if (fireVisual != null)
            fireVisual.SetActive(!pacified);

        if (pacifiedVisual != null)
            pacifiedVisual.SetActive(pacified);

        if (combustionVisual != null)
            combustionVisual.SetActive(combusting);
    }

    void OnDestroy()
    {
        ClearCameraEffect();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = new Color(1f, 0.45f, 0f, 0.75f);
        Gizmos.DrawWireSphere(transform.position, combustionDamageRadius);
    }
}
