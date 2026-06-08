using UnityEngine;
using UnityEngine.AI;

public class GoldenMouthBehavior : MonoBehaviour
{
    [Header("Help / Fire State")]
    public float timeToExtinguish = 20f;
    public float extinguishRequired = 100f;
    public bool chemicalWaterCountsAsPure = false;
    public GameObject fireVisual;
    public GameObject pacifiedVisual;

    [Header("Aggressive State")]
    public float chaseSpeed = 5.5f;
    public float wanderSpeed = 2.5f;
    public float wanderRadius = 12f;
    public float detectionRange = 12f;
    public float attackRange = 1.75f;
    public float damagePerAttack = 25f;
    public float attackCooldown = 1f;

    [Header("After Kill")]
    public GameObject willOWispPrefab;
    public Vector3 willOWispSpawnOffset = new Vector3(0f, 1f, 0f);

    [Header("Contamination / Fire Spread")]
    public GameObject fireHazardPrefab;
    public float fireHazardInterval = 1.5f;

    private NavMeshAgent agent;
    private Transform player;
    private PlayerStatus playerStatus;
    private float extinguishProgress;
    private float helpTimer;
    private float nextAttackTime;
    private float fireHazardTimer;
    private bool isAggressive;
    private bool isPacified;
    private bool spawnedDeathEffect;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        helpTimer = timeToExtinguish;
        ResolvePlayer();
        UpdateVisuals();
        SetWanderDestination();
    }

    void Update()
    {
        ResolvePlayer();

        if (isPacified) return;

        if (!isAggressive)
        {
            UpdateHelpTimer();
            return;
        }

        UpdateAggressiveBehavior();
        TryLeaveFireHazard();
    }

    void ResolvePlayer()
    {
        if (player != null && playerStatus != null) return;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject == null) return;

        player = playerObject.transform;
        playerStatus = playerObject.GetComponent<PlayerStatus>();
    }

    void UpdateHelpTimer()
    {
        helpTimer -= Time.deltaTime;
        if (helpTimer <= 0f)
            BecomeAggressive();
    }

    public void ApplyWater(WaterQuality quality, float amount)
    {
        if (isPacified || isAggressive || amount <= 0f) return;
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
        isPacified = true;
        isAggressive = false;
        StopAgent();
        UpdateVisuals();
    }

    void BecomeAggressive()
    {
        isAggressive = true;
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
            Wander();
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        if (distanceToPlayer <= detectionRange)
        {
            MoveTo(player.position, chaseSpeed);
            TryAttack(distanceToPlayer);
        }
        else
        {
            Wander();
        }
    }

    void TryAttack(float distanceToPlayer)
    {
        if (distanceToPlayer > attackRange || Time.time < nextAttackTime) return;

        bool killed = playerStatus.TakeDamage(damagePerAttack);
        nextAttackTime = Time.time + attackCooldown;

        if (killed)
            HandlePlayerKilled();
    }

    void HandlePlayerKilled()
    {
        if (spawnedDeathEffect) return;
        spawnedDeathEffect = true;

        if (willOWispPrefab != null && playerStatus != null)
            Instantiate(willOWispPrefab, playerStatus.transform.position + willOWispSpawnOffset, Quaternion.identity);

        if (playerStatus != null)
            playerStatus.ApplyDeathTransformation();
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
            agent.speed = isAggressive ? chaseSpeed : wanderSpeed;
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

    void FaceTarget(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f) return;

        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction.normalized), Time.deltaTime * 8f);
    }

    void TryLeaveFireHazard()
    {
        if (fireHazardPrefab == null) return;

        fireHazardTimer -= Time.deltaTime;
        if (fireHazardTimer > 0f) return;

        fireHazardTimer = fireHazardInterval;
        Instantiate(fireHazardPrefab, transform.position, Quaternion.identity);
    }

    void UpdateVisuals()
    {
        if (fireVisual != null)
            fireVisual.SetActive(!isPacified);

        if (pacifiedVisual != null)
            pacifiedVisual.SetActive(isPacified);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
