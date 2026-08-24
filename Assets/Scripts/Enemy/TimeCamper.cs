using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections;

public class TimeCamper : MonoBehaviour
{
    [Header("Detection")]
    public Transform player;
    public float detectionRadius = 2.5f;
    public float observationDistance = 18f;
    [Range(1f, 90f)] public float observationAngle = 28f;
    public bool requireLineOfSight = true;
    public LayerMask lineOfSightBlockingLayers = ~0;
    public float detectionEyeHeight = 1.4f;
    public float targetEyeHeight = 1.2f;

    [Header("Countdown")]
    public float minCountdown = 10f;
    public float maxCountdown = 10f;

    [Header("Damage")]
    public float damagePerSecond = 90f;
    public float damageDuration = 5f;
    public float damageRadius = 2.5f;

    [Header("Teleport")]
    public float teleportInterval = 45f;
    public float minReappearCooldown = 25f;
    public float maxReappearCooldown = 55f;

    [Header("Water Contamination")]
    public bool contaminateWaterOnMark = true;
    public float waterContaminationRadius = 0f;
    public float waterContaminationInterval = 0.5f;
    public float markContaminationLifetime = 0f;
    public LayerMask playerContaminationMask = ~0;

    [Header("Visuals")]
    public GameObject redCirclePrefab;
    public GameObject beamPrefab;
    public GameObject contaminationPrefab;
    public bool scaleWarningCircleToImpactRadius = true;
    public float warningCircleVisualDiameter = 5f;

    [Header("Animation Hooks")]
    public Animator animator;
    public string noticedTrigger = "Notice";
    public string beamTrigger = "Beam";
    public string disappearTrigger = "Disappear";
    public string reappearTrigger = "Reappear";
    public string countdownIntensityFloat = "CountdownIntensity";

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float countdownMinVignette = 0.18f;
    [Range(0f, 1f)] public float countdownMaxVignette = 0.75f;
    [Range(0f, 1f)] public float beamVignette = 0.8f;
    [Range(0f, 1f)] public float beamPulseIntensity = 0.9f;
    public float beamPulseDuration = 0.6f;
    public float countdownMaxShakeAmplitude = 0.7f;
    public float countdownShakeFrequency = 6f;
    public float beamShakeAmplitude = 1.6f;
    public float beamShakeFrequency = 14f;
    public float beamShakeDuration = 0.7f;

    [Header("Countdown UI")]
    public TextMeshProUGUI countdownText;

    [Header("Clone")]
    public bool isClone = false;
    public bool transformKnockedOutPlayerImmediately = false;

    private float countdownTimer;
    private float countdownDuration;
    private float damageTimer;
    private float teleportTimer;
    private float cooldownTimer;
    private bool isLeaving;
    private bool spawnedCloneForDeath;
    private bool eventWasTriggered;

    private GameObject redCircleInstance;
    private GameObject beamInstance;
    private PlayerStatus playerStatus;
    private NavMeshAgent agent;
    private PlayerMovement effectTargetMovement;
    private Renderer[] cachedRenderers;
    private Collider[] cachedColliders;

    enum State { WaitingToBeSeen, Countdown, Beam, Leaving, Cooldown }
    State currentState = State.WaitingToBeSeen;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        cachedColliders = GetComponentsInChildren<Collider>(true);
        ResolvePlayerReferences();
        ResolveCameraEffects();
        ResetCycle();
    }

    void Update()
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        ResolvePlayerReferences();
        ResolveCameraEffects();

        switch (currentState)
        {
            case State.WaitingToBeSeen:
                teleportTimer -= Time.deltaTime;
                if (TryGetObservingPlayer(out playerStatus, out player))
                    StartCountdown();
                else if (teleportTimer <= 0f)
                    StartLeaving();
                break;

            case State.Countdown:
                UpdateCountdown();
                break;

            case State.Beam:
                UpdateBeamEffect();
                damageTimer -= Time.deltaTime;
                DamagePlayerInRadius();
                if (damageTimer <= 0f)
                    StartLeaving();
                break;

            case State.Cooldown:
                cooldownTimer -= Time.deltaTime;
                if (cooldownTimer <= 0f)
                    ReappearForNewEncounter();
                break;
        }
    }

    public float GetImpactRadius()
    {
        return Mathf.Max(detectionRadius, damageRadius);
    }

    void ResolvePlayerReferences()
    {
        if (currentState != State.WaitingToBeSeen &&
            EnemyTargeting.IsValidTarget(playerStatus) &&
            player == playerStatus.transform)
        {
            return;
        }

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

    bool TryGetObservingPlayer(
        out PlayerStatus observingStatus,
        out Transform observingPlayer)
    {
        observingStatus = null;
        observingPlayer = null;

        PlayerStatus[] players =
            FindObjectsByType<PlayerStatus>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        float maxSqrDistance = observationDistance * observationDistance;
        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus candidate = players[i];
            if (!EnemyTargeting.IsValidTarget(candidate))
                continue;

            Transform candidateTransform = candidate.transform;
            if ((candidateTransform.position - transform.position).sqrMagnitude >
                maxSqrDistance)
            {
                continue;
            }

            if (!PlayerIsLookingAtTimeCamper(candidate, candidateTransform))
                continue;

            observingStatus = candidate;
            observingPlayer = candidateTransform;
            return true;
        }

        return false;
    }

    bool PlayerIsLookingAtTimeCamper(
        PlayerStatus candidateStatus,
        Transform candidateTransform)
    {
        Transform viewTransform = ResolvePlayerViewTransform(candidateTransform);
        Vector3 origin = viewTransform.position;
        Vector3 forward = viewTransform.forward;
        Vector3 target = transform.position + Vector3.up * detectionEyeHeight;
        Vector3 toTimeCamper = target - origin;
        float distance = toTimeCamper.magnitude;
        if (distance <= 0.01f)
            return true;

        float angle = Vector3.Angle(forward, toTimeCamper / distance);
        if (angle > observationAngle)
            return false;

        if (!requireLineOfSight)
            return true;

        return HasLineOfSightToTimeCamper(
            candidateStatus,
            origin,
            toTimeCamper / distance,
            distance);
    }

    Transform ResolvePlayerViewTransform(Transform candidateTransform)
    {
        Camera camera = candidateTransform.GetComponentInChildren<Camera>(true);
        if (camera != null && camera.isActiveAndEnabled)
            return camera.transform;

        return candidateTransform;
    }

    bool HasLineOfSightToTimeCamper(
        PlayerStatus candidateStatus,
        Vector3 origin,
        Vector3 direction,
        float distance)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            distance,
            lineOfSightBlockingLayers,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return true;

        float closestHitDistance = float.MaxValue;
        Collider closestCollider = null;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
                continue;
            if (candidateStatus != null &&
                (hitCollider.transform == candidateStatus.transform ||
                 hitCollider.transform.IsChildOf(candidateStatus.transform)))
            {
                continue;
            }

            if (hits[i].distance < closestHitDistance)
            {
                closestHitDistance = hits[i].distance;
                closestCollider = hitCollider;
            }
        }

        if (closestCollider == null)
            return true;

        return closestCollider.transform == transform ||
            closestCollider.transform.IsChildOf(transform);
    }

    void StartCountdown()
    {
        currentState = State.Countdown;
        countdownTimer = Random.Range(minCountdown, maxCountdown);
        countdownDuration = Mathf.Max(0.01f, countdownTimer);
        eventWasTriggered = true;
        SetAnimatorTrigger(noticedTrigger);
    }

    void UpdateCountdown()
    {
        countdownTimer -= Time.deltaTime;
        UpdateCountdownEffect();

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = Mathf.Ceil(countdownTimer).ToString();
        }

        if (countdownTimer <= 0f)
        {
            if (countdownText != null)
                countdownText.gameObject.SetActive(false);

            StartBeam();
        }
    }

    void UpdateCountdownEffect()
    {
        float progress = 1f - Mathf.Clamp01(countdownTimer / Mathf.Max(0.01f, countdownDuration));
        SetAnimatorFloat(countdownIntensityFloat, progress);
        EnemyPlayerEffects.SetThreatIntensity(
            ref effectTargetMovement,
            playerStatus,
            player,
            cameraEffects,
            Mathf.Lerp(countdownMinVignette, countdownMaxVignette, progress));
        EnemyPlayerEffects.Shake(
            ref effectTargetMovement,
            playerStatus,
            player,
            cameraEffects,
            countdownMaxShakeAmplitude * progress,
            countdownShakeFrequency,
            0.15f);
    }

    void StartBeam()
    {
        currentState = State.Beam;
        damageTimer = damageDuration;
        SetAnimatorTrigger(beamTrigger);

        EnemyPlayerEffects.Pulse(
            ref effectTargetMovement,
            playerStatus,
            player,
            cameraEffects,
            beamPulseIntensity,
            beamPulseDuration,
            beamShakeAmplitude,
            beamShakeFrequency,
            beamShakeDuration);

        if (beamPrefab != null)
            beamInstance = Instantiate(beamPrefab, transform.position, Quaternion.identity);
    }

    void UpdateBeamEffect()
    {
        EnemyPlayerEffects.SetThreatIntensity(
            ref effectTargetMovement,
            playerStatus,
            player,
            cameraEffects,
            beamVignette);
        EnemyPlayerEffects.Shake(
            ref effectTargetMovement,
            playerStatus,
            player,
            cameraEffects,
            beamShakeAmplitude * 0.35f,
            beamShakeFrequency,
            0.15f);
    }

    void DamagePlayerInRadius()
    {
        PlayerStatus[] players =
            FindObjectsByType<PlayerStatus>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus targetStatus = players[i];
            if (!EnemyTargeting.IsValidTarget(targetStatus, requireCanAct: false))
                continue;

            float dist = Vector3.Distance(transform.position, targetStatus.transform.position);
            if (dist > damageRadius)
                continue;

            bool knockedOutPlayer = targetStatus.TakeDamage(damagePerSecond * Time.deltaTime);
            if (!knockedOutPlayer)
                continue;

            if (transformKnockedOutPlayerImmediately)
                TransformKilledPlayerIntoClone(targetStatus);

            StartLeaving();
            return;
        }
    }

    void TransformKilledPlayerIntoClone(PlayerStatus killedPlayer)
    {
        if (spawnedCloneForDeath) return;
        if (EnemySpawner.Instance == null || killedPlayer == null) return;
        if (TimeCamperManager.Instance == null || !TimeCamperManager.Instance.CanSpawn()) return;

        Vector3 clonePosition = killedPlayer.transform.position;
        if (!killedPlayer.ForceTransformDeath()) return;

        spawnedCloneForDeath = true;
        EnemySpawner.Instance.SpawnTimeCamperAt(clonePosition, isClone: true);
    }

    void StartLeaving()
    {
        if (isLeaving) return;
        ClearCameraEffect();
        StartCoroutine(LeaveAndTeleport());
    }

    IEnumerator LeaveAndTeleport()
    {
        isLeaving = true;
        currentState = State.Leaving;

        SetAnimatorTrigger(disappearTrigger);
        CleanupVisuals();
        if (eventWasTriggered)
            LeaveContaminationMark();

        SetVisible(false);

        cooldownTimer = Random.Range(
            Mathf.Min(minReappearCooldown, maxReappearCooldown),
            Mathf.Max(minReappearCooldown, maxReappearCooldown));
        yield return new WaitForSeconds(0.1f);

        currentState = State.Cooldown;
    }

    void ReappearForNewEncounter()
    {
        Vector3 newPos;
        if (EnemySpawner.Instance == null ||
            !EnemySpawner.Instance.TryGetTimeCamperEncounterPosition(out newPos))
        {
            if (EnemySpawner.Instance == null ||
                !EnemySpawner.Instance.TryGetValidSpawnPosition(out newPos))
            {
                Destroy(gameObject);
                return;
            }
        }

        TeleportTo(newPos);
        SetVisible(true);
        SetAnimatorTrigger(reappearTrigger);
        ResetCycle();
    }

    void TeleportTo(Vector3 newPos)
    {
        if (agent != null && agent.enabled)
            agent.Warp(newPos);
        else
            transform.position = newPos;
    }

    void ResetCycle()
    {
        isLeaving = false;
        spawnedCloneForDeath = false;
        eventWasTriggered = false;
        currentState = State.WaitingToBeSeen;
        teleportTimer = teleportInterval;
        damageTimer = 0f;
        cooldownTimer = 0f;
        SetAnimatorFloat(countdownIntensityFloat, 0f);
        ClearCameraEffect();

        if (countdownText != null)
            countdownText.gameObject.SetActive(false);

        SpawnWarningCircle();
    }

    void SetVisible(bool visible)
    {
        if (cachedRenderers == null || cachedRenderers.Length == 0)
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
        if (cachedColliders == null || cachedColliders.Length == 0)
            cachedColliders = GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            if (cachedRenderers[i] != null)
                cachedRenderers[i].enabled = visible;
        }

        for (int i = 0; i < cachedColliders.Length; i++)
        {
            if (cachedColliders[i] != null)
                cachedColliders[i].enabled = visible;
        }

        if (agent != null)
            agent.enabled = visible;
    }

    void ClearCameraEffect()
    {
        EnemyPlayerEffects.ClearThreat(ref effectTargetMovement, cameraEffects, true);
    }

    void SetAnimatorTrigger(string triggerName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(triggerName))
            return;

        animator.SetTrigger(triggerName);
    }

    void SetAnimatorFloat(string floatName, float value)
    {
        if (animator == null || string.IsNullOrWhiteSpace(floatName))
            return;

        animator.SetFloat(floatName, value);
    }

    void CleanupVisuals()
    {
        if (redCircleInstance != null) Destroy(redCircleInstance);
        if (beamInstance != null) Destroy(beamInstance);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
    }

    void SpawnWarningCircle()
    {
        if (redCirclePrefab == null) return;

        redCircleInstance = Instantiate(redCirclePrefab,
            new Vector3(transform.position.x, transform.position.y + 0.05f, transform.position.z),
            Quaternion.identity);

        if (scaleWarningCircleToImpactRadius && warningCircleVisualDiameter > 0.01f)
        {
            float diameter = GetImpactRadius() * 2f;
            float scale = diameter / warningCircleVisualDiameter;
            Vector3 localScale = redCircleInstance.transform.localScale;
            redCircleInstance.transform.localScale =
                new Vector3(localScale.x * scale, localScale.y, localScale.z * scale);
        }
    }

    void LeaveContaminationMark()
    {
        GameObject mark = null;

        if (contaminationPrefab != null)
        {
            mark = Instantiate(contaminationPrefab,
                new Vector3(transform.position.x, transform.position.y + 0.05f, transform.position.z),
                Quaternion.identity);
        }

        if (!contaminateWaterOnMark) return;

        if (mark == null)
            mark = new GameObject("TimeCamperWaterContaminationZone");

        mark.transform.position = new Vector3(transform.position.x, transform.position.y + 0.05f, transform.position.z);

        WaterContaminationZone zone = mark.GetComponent<WaterContaminationZone>();
        if (zone == null)
            zone = mark.AddComponent<WaterContaminationZone>();

        zone.radius = GetContaminationRadius();
        zone.contaminationQuality = WaterQuality.Contaminated;
        zone.contaminateInterval = waterContaminationInterval;
        zone.lifetime = markContaminationLifetime;
        zone.playerMask = playerContaminationMask;
    }

    float GetContaminationRadius()
    {
        return waterContaminationRadius > 0f ? waterContaminationRadius : detectionRadius;
    }

    void OnDestroy()
    {
        ClearCameraEffect();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = new Color(1f, 0.3f, 0f);
        Gizmos.DrawWireSphere(transform.position, damageRadius);
        Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, GetContaminationRadius());
    }
}
