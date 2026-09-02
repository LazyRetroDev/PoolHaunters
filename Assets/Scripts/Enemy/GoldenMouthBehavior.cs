using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class GoldenMouthBehavior : MonoBehaviour
{
    enum GoldenMouthState
    {
        WaitingToBeSeen,
        WaitingForHelp,
        Combusting,
        Aggressive,
        Searching,
        Pacified
    }

    [Header("Hidden Encounter Start")]
    public bool relocateNearPlayerOnSpawn = true;
    public float hiddenSpawnMinDistance = 6f;
    public float hiddenSpawnMaxDistance = 16f;
    public int hiddenSpawnAttempts = 28;
    [Range(1f, 179f)] public float playerNoticeFieldOfView = 80f;
    public float playerEyeHeight = 1.6f;
    public float goldenMouthEyeHeight = 1f;
    public LayerMask visibilityBlockingLayers = ~0;
    public float passiveFollowSpeed = 2.1f;
    public float passiveFollowDistance = 4f;
    public bool facePlayerWhileUnseen = false;

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
    public float searchDurationAfterLost = 6f;
    public float searchWanderRadius = 3f;
    public float postWispAggressionDuration = 10f;

    [Header("After Kill")]
    public GameObject willOWispPrefab;
    public Vector3 willOWispSpawnOffset = new Vector3(0f, 1f, 0f);
    public bool transformKnockedOutPlayerImmediately = false;

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

    [Header("Disappear")]
    public float pacifiedSpinDuration = 0.8f;
    public float pacifiedSpinDegrees = 360f;
    public float disappearDelay = 0.9f;

    private readonly HashSet<PlayerStatus> transformedPlayers = new HashSet<PlayerStatus>();
    private NavMeshAgent agent;
    private Transform player;
    private PlayerStatus playerStatus;
    private PlayerStatus noticedByPlayer;
    private GoldenMouthState state = GoldenMouthState.WaitingToBeSeen;
    private float extinguishProgress;
    private float helpTimer;
    private float combustionTimer;
    private float nextAttackTime;
    private float fireHazardTimer;
    private float searchTimer;
    private float pacifiedTimer;
    private float pacifiedElapsed;
    private float forcedAggressiveUntil;
    private bool dealtCombustionDamage;
    private bool hasLastKnownPlayerPosition;
    private Vector3 lastKnownPlayerPosition;
    private PlayerMovement effectTargetMovement;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        ResolvePlayer();
        TryRelocateNearPlayerOutOfSight();
        ResolveCameraEffects();
        UpdateVisuals();
        StopAgent();
    }

    void Update()
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        ResolvePlayer();
        ResolveCameraEffects();

        switch (state)
        {
            case GoldenMouthState.WaitingToBeSeen:
                UpdateWaitingToBeSeenState();
                break;
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
            case GoldenMouthState.Searching:
                UpdateSearchingState();
                TryLeaveFireHazard();
                break;
            case GoldenMouthState.Pacified:
                UpdatePacifiedState();
                break;
        }
    }

    void ResolvePlayer()
    {
        if (EnemyTargeting.TryFindClosestPlayer(
            transform.position,
            out playerStatus,
            out player))
        {
            return;
        }

        player = null;
        playerStatus = null;
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindAnyObjectByType<PlayerVignetteEffect>();
    }

    void UpdateHelpState()
    {
        helpTimer -= Time.deltaTime;

        if (noticedByPlayer != null &&
            EnemyTargeting.IsValidTarget(noticedByPlayer, requireCanAct: false))
        {
            playerStatus = noticedByPlayer;
            player = noticedByPlayer.transform;
        }

        if (moveWhileWaitingForHelp && player != null)
            FollowPassively();
        else if (moveWhileWaitingForHelp)
            Wander(helpWanderSpeed);
        else
            StopAgent();

        if (helpTimer <= 0f)
            StartCombustion();
    }

    public void ApplyWater(WaterQuality quality, float amount)
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

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
        pacifiedElapsed = 0f;
        pacifiedTimer = Mathf.Max(disappearDelay, pacifiedSpinDuration);
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

        EnemyPlayerEffects.Pulse(
            ref effectTargetMovement,
            playerStatus,
            player,
            cameraEffects,
            combustionPulseIntensity,
            combustionPulseDuration,
            combustionShakeAmplitude,
            combustionShakeFrequency,
            combustionShakeDuration);

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
        if (player != null)
        {
            lastKnownPlayerPosition = player.position;
            hasLastKnownPlayerPosition = true;
        }
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
            if (IsAggressionExtended())
            {
                ContinueExtendedAggression();
                return;
            }

            BeginSearch();
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        UpdateAggressiveCameraEffect(distanceToPlayer);

        bool canSeePlayer = distanceToPlayer <= detectionRange &&
            HasEnemyLineOfSightToPlayer(playerStatus);
        if (canSeePlayer)
        {
            lastKnownPlayerPosition = player.position;
            hasLastKnownPlayerPosition = true;
            MoveTo(player.position, chaseSpeed);
            TryAttack(distanceToPlayer);
            return;
        }

        if (distanceToPlayer >= loseInterestDistance)
        {
            ClearCameraEffect();
            if (IsAggressionExtended())
            {
                ContinueExtendedAggression();
                return;
            }

            BeginSearch();
        }
        else
        {
            if (hasLastKnownPlayerPosition)
                MoveTo(lastKnownPlayerPosition, wanderSpeed);
            else
                BeginSearch();
        }
    }

    void UpdateWaitingToBeSeenState()
    {
        StopAgent();

        PlayerStatus observer;
        if (TryFindObservingPlayer(out observer))
        {
            BeginHelpCountdown(observer);
            return;
        }

        if (facePlayerWhileUnseen && player != null)
            FaceTarget(player.position);
    }

    void BeginHelpCountdown(PlayerStatus observer)
    {
        noticedByPlayer = observer;
        playerStatus = observer;
        player = observer != null ? observer.transform : player;
        helpTimer = timeToExtinguish;
        extinguishProgress = 0f;
        state = GoldenMouthState.WaitingForHelp;
        UpdateVisuals();
    }

    void FollowPassively()
    {
        if (player == null)
        {
            StopAgent();
            return;
        }

        FaceTarget(player.position);
        float distance = Vector3.Distance(transform.position, player.position);
        if (distance <= passiveFollowDistance)
        {
            StopAgent();
            return;
        }

        MoveTo(player.position, passiveFollowSpeed);
    }

    void BeginSearch()
    {
        ClearCameraEffect();
        state = GoldenMouthState.Searching;
        searchTimer = Mathf.Max(0.1f, searchDurationAfterLost);

        if (!hasLastKnownPlayerPosition)
            lastKnownPlayerPosition = transform.position;

        MoveTo(lastKnownPlayerPosition, wanderSpeed);
        UpdateVisuals();
    }

    void UpdateSearchingState()
    {
        if (player != null &&
            playerStatus != null &&
            Vector3.Distance(transform.position, player.position) <= detectionRange &&
            HasEnemyLineOfSightToPlayer(playerStatus))
        {
            BecomeAggressive();
            return;
        }

        if (IsAggressionExtended())
        {
            state = GoldenMouthState.Aggressive;
            ContinueExtendedAggression();
            return;
        }

        searchTimer -= Time.deltaTime;
        if (searchTimer <= 0f)
        {
            Disappear();
            return;
        }

        if (agent != null &&
            agent.enabled &&
            agent.isOnNavMesh &&
            !agent.pathPending &&
            agent.remainingDistance <= agent.stoppingDistance)
        {
            Vector3 searchPoint = lastKnownPlayerPosition +
                Random.insideUnitSphere * Mathf.Max(0.1f, searchWanderRadius);
            searchPoint.y = lastKnownPlayerPosition.y;
            if (NavMesh.SamplePosition(searchPoint, out NavMeshHit hit, searchWanderRadius, NavMesh.AllAreas))
                MoveTo(hit.position, wanderSpeed);
        }
    }

    void UpdatePacifiedState()
    {
        ClearCameraEffect();
        pacifiedElapsed += Time.deltaTime;
        pacifiedTimer -= Time.deltaTime;

        float spinDuration = Mathf.Max(0.01f, pacifiedSpinDuration);
        if (pacifiedElapsed <= spinDuration)
        {
            float degreesPerSecond = pacifiedSpinDegrees / spinDuration;
            transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.World);
        }

        if (pacifiedTimer <= 0f)
            Disappear();
    }

    void UpdateAggressiveCameraEffect(float distanceToPlayer)
    {
        if (distanceToPlayer > loseInterestDistance)
        {
            ClearCameraEffect();
            return;
        }

        float danger = 1f - Mathf.Clamp01(distanceToPlayer / Mathf.Max(0.01f, detectionRange));
        EnemyPlayerEffects.SetThreatIntensity(
            ref effectTargetMovement,
            playerStatus,
            player,
            cameraEffects,
            Mathf.Lerp(aggressiveMinVignette, aggressiveMaxVignette, danger));
        EnemyPlayerEffects.Shake(
            ref effectTargetMovement,
            playerStatus,
            player,
            cameraEffects,
            aggressiveMaxShakeAmplitude * danger,
            aggressiveShakeFrequency,
            0.15f);
    }

    void TryAttack(float distanceToPlayer)
    {
        if (distanceToPlayer > attackRange || Time.time < nextAttackTime) return;

        EnemyPlayerEffects.Shake(
            ref effectTargetMovement,
            playerStatus,
            player,
            cameraEffects,
            attackShakeAmplitude,
            attackShakeFrequency,
            attackShakeDuration);

        bool knockedOut = playerStatus.TakeDamage(damagePerAttack);
        nextAttackTime = Time.time + attackCooldown;

        if (knockedOut && transformKnockedOutPlayerImmediately)
            HandlePlayerKilled(playerStatus);
    }

    void HandlePlayerKilled(PlayerStatus killedPlayer)
    {
        if (killedPlayer == null || transformedPlayers.Contains(killedPlayer)) return;
        if (!killedPlayer.ForceTransformDeath()) return;

        transformedPlayers.Add(killedPlayer);
        ExtendAggressionAfterWisp(killedPlayer);

        if (willOWispPrefab != null)
            Instantiate(willOWispPrefab, killedPlayer.transform.position + willOWispSpawnOffset, Quaternion.identity);
    }

    void ExtendAggressionAfterWisp(PlayerStatus transformedPlayer)
    {
        state = GoldenMouthState.Aggressive;
        forcedAggressiveUntil = Time.time + Mathf.Max(0f, postWispAggressionDuration);
        searchTimer = Mathf.Max(searchTimer, searchDurationAfterLost);
        fireHazardTimer = 0f;

        if (transformedPlayer != null)
        {
            lastKnownPlayerPosition = transformedPlayer.transform.position;
            hasLastKnownPlayerPosition = true;
        }

        ClearCameraEffect();
        UpdateVisuals();
    }

    bool IsAggressionExtended()
    {
        return Time.time < forcedAggressiveUntil;
    }

    void ContinueExtendedAggression()
    {
        if (hasLastKnownPlayerPosition)
            MoveTo(lastKnownPlayerPosition, wanderSpeed);
        else
            Wander(wanderSpeed);
    }

    bool TryFindObservingPlayer(out PlayerStatus observer)
    {
        observer = null;
        PlayerStatus[] players = FindObjectsByType<PlayerStatus>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus candidate = players[i];
            if (!EnemyTargeting.IsValidTarget(candidate, requireCanAct: false))
                continue;
            if (!CanPlayerSeeGoldenMouth(candidate))
                continue;

            float distance = Vector3.Distance(candidate.transform.position, transform.position);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            observer = candidate;
        }

        return observer != null;
    }

    bool CanPlayerSeeGoldenMouth(PlayerStatus status)
    {
        return CanPlayerSeePoint(
            status,
            transform.position + Vector3.up * goldenMouthEyeHeight,
            transform);
    }

    bool CanPlayerSeePoint(PlayerStatus status, Vector3 target, Transform targetRoot = null)
    {
        if (status == null)
            return false;

        Transform view = FindPlayerView(status);
        Vector3 eye = view != null
            ? view.position
            : status.transform.position + Vector3.up * playerEyeHeight;
        Vector3 forward = view != null
            ? view.forward
            : status.transform.forward;
        Vector3 direction = target - eye;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
            return true;

        if (Vector3.Angle(forward, direction) > playerNoticeFieldOfView * 0.5f)
            return false;

        return HasLineOfSight(
            eye,
            direction / distance,
            distance,
            status.transform,
            targetRoot);
    }

    Transform FindPlayerView(PlayerStatus status)
    {
        if (status == null)
            return null;

        Camera[] cameras = status.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].enabled && cameras[i].gameObject.activeInHierarchy)
                return cameras[i].transform;
        }

        return null;
    }

    bool HasEnemyLineOfSightToPlayer(PlayerStatus status)
    {
        if (status == null)
            return false;

        Vector3 eye = transform.position + Vector3.up * goldenMouthEyeHeight;
        Vector3 target = status.transform.position + Vector3.up * playerEyeHeight;
        Vector3 direction = target - eye;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
            return true;

        return HasLineOfSight(
            eye,
            direction / distance,
            distance,
            transform,
            status.transform);
    }

    bool HasLineOfSight(
        Vector3 origin,
        Vector3 direction,
        float distance,
        Transform ignoredRoot,
        Transform targetRoot)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            distance,
            visibilityBlockingLayers,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == null)
                continue;
            if (ignoredRoot != null &&
                (hitTransform == ignoredRoot || hitTransform.IsChildOf(ignoredRoot)))
            {
                continue;
            }
            if (targetRoot != null &&
                (hitTransform == targetRoot || hitTransform.IsChildOf(targetRoot)))
            {
                return true;
            }

            return false;
        }

        return true;
    }

    void TryRelocateNearPlayerOutOfSight()
    {
        if (!relocateNearPlayerOnSpawn || playerStatus == null)
            return;
        if (agent == null || !agent.enabled)
            return;

        PlayerStatus[] players = FindObjectsByType<PlayerStatus>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        int attempts = Mathf.Max(1, hiddenSpawnAttempts);
        float minDistance = Mathf.Max(0f, hiddenSpawnMinDistance);
        float maxDistance = Mathf.Max(minDistance + 0.1f, hiddenSpawnMaxDistance);

        for (int i = 0; i < attempts; i++)
        {
            Vector2 circle = Random.insideUnitCircle.normalized;
            if (circle.sqrMagnitude <= 0.001f)
                circle = Random.insideUnitCircle.normalized;
            float distance = Random.Range(minDistance, maxDistance);
            Vector3 candidate = playerStatus.transform.position +
                new Vector3(circle.x, 0f, circle.y) * distance;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                continue;
            if (IsVisibleToAnyPlayer(hit.position, players))
                continue;

            if (agent.isOnNavMesh)
                agent.Warp(hit.position);
            else
                transform.position = hit.position;

            FaceTarget(playerStatus.transform.position);
            return;
        }
    }

    bool IsVisibleToAnyPlayer(Vector3 position, PlayerStatus[] players)
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (!EnemyTargeting.IsValidTarget(players[i], requireCanAct: false))
                continue;
            if (CanPlayerSeePoint(
                players[i],
                position + Vector3.up * goldenMouthEyeHeight))
            {
                return true;
            }
        }

        return false;
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

            bool knockedOut = status.TakeDamage(combustionDamage);
            if (knockedOut && transformKnockedOutPlayerImmediately)
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
        EnemyPlayerEffects.ClearThreat(ref effectTargetMovement, cameraEffects, true);
    }

    void Disappear()
    {
        ClearCameraEffect();
        StopAgent();

        NetworkObject networkObject = GetComponent<NetworkObject>();
        if (networkObject != null &&
            networkObject.IsSpawned &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsServer)
        {
            networkObject.Despawn(true);
            return;
        }

        Destroy(gameObject);
    }

    void UpdateVisuals()
    {
        bool pacified = state == GoldenMouthState.Pacified;
        bool combusting = state == GoldenMouthState.Combusting;
        bool vanished = pacified;

        if (fireVisual != null)
            fireVisual.SetActive(!vanished);

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
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, hiddenSpawnMinDistance);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, hiddenSpawnMaxDistance);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = new Color(1f, 0.45f, 0f, 0.75f);
        Gizmos.DrawWireSphere(transform.position, combustionDamageRadius);
    }
}
