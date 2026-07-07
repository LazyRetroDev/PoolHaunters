using UnityEngine;
using UnityEngine.AI;

public class VictoriaRegiaBehavior : MonoBehaviour
{
    enum VictoriaRegiaState
    {
        Submerged,
        Rising,
        Stalking,
        Grappling,
        Escaping
    }

    [Header("Visual State")]
    public GameObject submergedVisualRoot;
    public GameObject glowingEyesRoot;
    public GameObject emergedVisualRoot;
    public Transform bodyRoot;
    public float submergedLocalY = -1.2f;
    public float emergedLocalY = 0f;

    [Header("Attention Counter")]
    public float playerLookAngle = 18f;
    public float gazeCheckDistance = 18f;
    public LayerMask gazeBlockers = ~0;
    public bool requireLineOfSight = true;

    [Header("Rising")]
    public float timeUnwatchedToRise = 1.25f;
    public float riseDuration = 3f;
    public float watchedSinkSpeed = 2.5f;

    [Header("Stalking")]
    public float stalkSpeed = 3.25f;
    public float behindPlayerDistance = 2.4f;
    public float behindPositionRefreshInterval = 0.25f;
    public float grappleRange = 1.45f;
    public float abandonDistance = 24f;

    [Header("Grapple")]
    public Transform grappleHoldPoint;
    public Vector3 grappleHoldOffset = new Vector3(0f, 1.1f, 0.6f);
    public float grappleWindup = 0.35f;
    public bool blockPlayerControlsWhileGrappled = true;

    [Header("Escape / Kill")]
    public float escapeSpeed = 6.5f;
    public Transform[] escapePoints;
    public float fallbackEscapeDistance = 10f;
    public float escapeReachedDistance = 1.5f;
    public bool instantKillBypassesKnockout = true;
    public bool sinkAfterKill = true;

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float risingVignette = 0.25f;
    [Range(0f, 1f)] public float stalkingVignette = 0.45f;
    [Range(0f, 1f)] public float grappleVignette = 0.8f;
    public float grappleShakeAmplitude = 1f;
    public float grappleShakeFrequency = 14f;
    public float grappleShakeDuration = 0.35f;

    private NavMeshAgent agent;
    private VictoriaRegiaState state = VictoriaRegiaState.Submerged;
    private Transform targetPlayer;
    private PlayerStatus targetStatus;
    private float unwatchedTimer;
    private float riseTimer;
    private float behindRefreshTimer;
    private float grappleTimer;
    private Vector3 currentBehindTarget;
    private Vector3 escapeTarget;
    private PlayerMovement grappledMovement;
    private PlayerInventory grappledInventory;
    private WaterCannon grappledWaterCannon;
    private Rigidbody grappledRigidbody;
    private bool grappledMovementWasEnabled;
    private bool grappledInventoryWasEnabled;
    private bool grappledWaterCannonWasEnabled;
    private bool grappledRigidbodyWasKinematic;
    private bool grappledRigidbodyUsedGravity;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        ResolveCameraEffects();
        EnterSubmerged();
    }

    void Update()
    {
        ResolveCameraEffects();
        ResolveTarget();

        switch (state)
        {
            case VictoriaRegiaState.Submerged:
                UpdateSubmerged();
                break;
            case VictoriaRegiaState.Rising:
                UpdateRising();
                break;
            case VictoriaRegiaState.Stalking:
                UpdateStalking();
                break;
            case VictoriaRegiaState.Grappling:
                UpdateGrappling();
                break;
            case VictoriaRegiaState.Escaping:
                UpdateEscaping();
                break;
        }
    }

    void ResolveTarget()
    {
        if (state == VictoriaRegiaState.Grappling || state == VictoriaRegiaState.Escaping)
            return;

        PlayerStatus[] players = FindObjectsOfType<PlayerStatus>();
        float bestDistance = float.PositiveInfinity;
        targetStatus = null;
        targetPlayer = null;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus status = players[i];
            if (status == null || status.IsDead()) continue;

            float distance = Vector3.Distance(transform.position, status.transform.position);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            targetStatus = status;
            targetPlayer = status.transform;
        }
    }

    Transform FindPlayerCamera(Transform playerRoot)
    {
        if (playerRoot == null) return null;

        Camera[] cameras = playerRoot.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].enabled)
                return cameras[i].transform;
        }

        return playerRoot;
    }

    void UpdateSubmerged()
    {
        StopAgent();
        MoveBodyToward(submergedLocalY, watchedSinkSpeed);
        ClearCameraEffect();

        if (targetPlayer == null)
        {
            unwatchedTimer = 0f;
            return;
        }

        if (IsAnyPlayerLookingAtMe())
        {
            unwatchedTimer = 0f;
            return;
        }

        unwatchedTimer += Time.deltaTime;
        if (unwatchedTimer >= timeUnwatchedToRise)
            BeginRising();
    }

    void BeginRising()
    {
        state = VictoriaRegiaState.Rising;
        riseTimer = 0f;
        UpdateVisuals();
    }

    void UpdateRising()
    {
        StopAgent();

        if (targetPlayer == null)
        {
            EnterSubmerged();
            return;
        }

        if (IsAnyPlayerLookingAtMe())
        {
            riseTimer -= Time.deltaTime * watchedSinkSpeed;
            riseTimer = Mathf.Max(0f, riseTimer);
            MoveBodyByRisePercent();
            UpdateCameraEffect(risingVignette);

            if (riseTimer <= 0f)
                EnterSubmerged();

            return;
        }

        riseTimer += Time.deltaTime;
        MoveBodyByRisePercent();
        UpdateCameraEffect(risingVignette);

        if (riseTimer >= riseDuration)
            BeginStalking();
    }

    void BeginStalking()
    {
        state = VictoriaRegiaState.Stalking;
        behindRefreshTimer = 0f;
        SetAgentStopped(false);
        UpdateVisuals();
    }

    void UpdateStalking()
    {
        if (targetPlayer == null || targetStatus == null)
        {
            EnterSubmerged();
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, targetPlayer.position);
        UpdateCameraEffect(stalkingVignette);

        if (distanceToPlayer > abandonDistance)
        {
            EnterSubmerged();
            return;
        }

        if (IsAnyPlayerLookingAtMe())
        {
            EnterSubmerged();
            return;
        }

        behindRefreshTimer -= Time.deltaTime;
        if (behindRefreshTimer <= 0f)
        {
            behindRefreshTimer = behindPositionRefreshInterval;
            currentBehindTarget = GetBehindPlayerPosition();
        }

        MoveTo(currentBehindTarget, stalkSpeed);

        if (distanceToPlayer <= grappleRange)
            BeginGrapple();
    }

    void BeginGrapple()
    {
        if (targetStatus == null || targetStatus.IsDead()) return;

        state = VictoriaRegiaState.Grappling;
        grappleTimer = grappleWindup;
        StoreAndBlockVictimControls(targetStatus);
        StopAgent();

        if (cameraEffects != null)
        {
            cameraEffects.Pulse(grappleVignette, grappleShakeDuration);
            cameraEffects.Shake(grappleShakeAmplitude, grappleShakeFrequency, grappleShakeDuration);
        }

        UpdateVisuals();
    }

    void UpdateGrappling()
    {
        if (targetPlayer == null || targetStatus == null || targetStatus.IsDead())
        {
            EnterSubmerged();
            return;
        }

        HoldGrappledPlayer();
        FaceTarget(targetPlayer.position);
        UpdateCameraEffect(grappleVignette);

        grappleTimer -= Time.deltaTime;
        if (grappleTimer <= 0f)
            BeginEscape();
    }

    void BeginEscape()
    {
        escapeTarget = GetEscapeTarget();
        state = VictoriaRegiaState.Escaping;
        SetAgentStopped(false);

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.speed = escapeSpeed;
            agent.SetDestination(escapeTarget);
        }
    }

    void UpdateEscaping()
    {
        if (targetPlayer == null || targetStatus == null)
        {
            EnterSubmerged();
            return;
        }

        HoldGrappledPlayer();
        MoveTo(escapeTarget, escapeSpeed);
        UpdateCameraEffect(grappleVignette);

        if (Vector3.Distance(transform.position, escapeTarget) <= escapeReachedDistance)
            KillGrappledPlayer();
    }

    void KillGrappledPlayer()
    {
        PlayerStatus victim = targetStatus;
        RestoreVictimControls(victim);

        if (victim != null && !victim.IsDead())
        {
            if (instantKillBypassesKnockout)
                victim.Die();
            else
                victim.TakeDamage(victim.GetMaxHealth());
        }

        targetStatus = null;
        targetPlayer = null;
        ClearVictimReferences();

        if (sinkAfterKill)
            EnterSubmerged();
        else
            BeginStalking();
    }

    bool IsAnyPlayerLookingAtMe()
    {
        PlayerStatus[] players = FindObjectsOfType<PlayerStatus>();
        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus status = players[i];
            if (status == null || status.IsDead()) continue;

            Transform view = FindPlayerCamera(status.transform);
            if (view != null && IsViewLookingAtMe(view))
                return true;
        }

        return false;
    }

    bool IsViewLookingAtMe(Transform view)
    {
        Vector3 origin = view.position;
        Vector3 targetPoint = GetLookPoint();
        Vector3 toMe = targetPoint - origin;
        float distance = toMe.magnitude;
        if (distance > gazeCheckDistance || distance <= 0.01f) return false;

        float angle = Vector3.Angle(view.forward, toMe.normalized);
        if (angle > playerLookAngle) return false;

        if (!requireLineOfSight) return true;

        RaycastHit hit;
        if (!Physics.Raycast(origin, toMe.normalized, out hit, distance, gazeBlockers, QueryTriggerInteraction.Ignore))
            return true;

        return hit.collider != null && hit.collider.transform.IsChildOf(transform);
    }

    Vector3 GetLookPoint()
    {
        if (glowingEyesRoot != null)
            return glowingEyesRoot.transform.position;

        return transform.position + Vector3.up * 1.1f;
    }

    Vector3 GetBehindPlayerPosition()
    {
        if (targetPlayer == null)
            return transform.position;

        Vector3 wanted = targetPlayer.position - targetPlayer.forward * behindPlayerDistance;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(wanted, out hit, behindPlayerDistance + 2f, NavMesh.AllAreas))
            return hit.position;

        return wanted;
    }

    Vector3 GetEscapeTarget()
    {
        Transform point = ChooseEscapePoint();
        if (point != null)
            return point.position;

        Vector3 away = targetPlayer != null
            ? transform.position - targetPlayer.position
            : -transform.forward;
        away.y = 0f;
        if (away.sqrMagnitude <= 0.001f)
            away = -transform.forward;

        Vector3 wanted = transform.position + away.normalized * fallbackEscapeDistance;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(wanted, out hit, fallbackEscapeDistance, NavMesh.AllAreas))
            return hit.position;

        return wanted;
    }

    Transform ChooseEscapePoint()
    {
        if (escapePoints == null || escapePoints.Length == 0)
            return null;

        int start = Random.Range(0, escapePoints.Length);
        for (int i = 0; i < escapePoints.Length; i++)
        {
            Transform point = escapePoints[(start + i) % escapePoints.Length];
            if (point != null)
                return point;
        }

        return null;
    }

    void StoreAndBlockVictimControls(PlayerStatus victim)
    {
        if (!blockPlayerControlsWhileGrappled || victim == null) return;

        grappledMovement = victim.GetComponent<PlayerMovement>();
        grappledInventory = victim.GetComponent<PlayerInventory>();
        grappledWaterCannon = victim.GetComponentInChildren<WaterCannon>();
        grappledRigidbody = victim.GetComponent<Rigidbody>();

        if (grappledMovement != null)
        {
            grappledMovementWasEnabled = grappledMovement.enabled;
            grappledMovement.enabled = false;
        }

        if (grappledInventory != null)
        {
            grappledInventoryWasEnabled = grappledInventory.enabled;
            grappledInventory.enabled = false;
        }

        if (grappledWaterCannon != null)
        {
            grappledWaterCannonWasEnabled = grappledWaterCannon.enabled;
            grappledWaterCannon.enabled = false;
        }

        if (grappledRigidbody != null)
        {
            grappledRigidbodyWasKinematic = grappledRigidbody.isKinematic;
            grappledRigidbodyUsedGravity = grappledRigidbody.useGravity;
            grappledRigidbody.linearVelocity = Vector3.zero;
            grappledRigidbody.angularVelocity = Vector3.zero;
            grappledRigidbody.useGravity = false;
            grappledRigidbody.isKinematic = true;
        }
    }

    void RestoreVictimControls(PlayerStatus victim)
    {
        if (!blockPlayerControlsWhileGrappled) return;
        bool canRestore = victim != null && victim.CanAct();

        if (grappledMovement != null)
            grappledMovement.enabled = canRestore && grappledMovementWasEnabled;

        if (grappledInventory != null)
            grappledInventory.enabled = canRestore && grappledInventoryWasEnabled;

        if (grappledWaterCannon != null)
            grappledWaterCannon.enabled = canRestore && grappledWaterCannonWasEnabled;

        if (grappledRigidbody != null && canRestore)
        {
            grappledRigidbody.isKinematic = grappledRigidbodyWasKinematic;
            grappledRigidbody.useGravity = grappledRigidbodyUsedGravity;
        }
    }

    void ClearVictimReferences()
    {
        grappledMovement = null;
        grappledInventory = null;
        grappledWaterCannon = null;
        grappledRigidbody = null;
        grappledMovementWasEnabled = false;
        grappledInventoryWasEnabled = false;
        grappledWaterCannonWasEnabled = false;
    }

    void HoldGrappledPlayer()
    {
        if (targetPlayer == null) return;

        Vector3 holdPosition = grappleHoldPoint != null
            ? grappleHoldPoint.position
            : transform.TransformPoint(grappleHoldOffset);

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

    void EnterSubmerged()
    {
        RestoreVictimControls(targetStatus);
        ClearVictimReferences();
        state = VictoriaRegiaState.Submerged;
        unwatchedTimer = 0f;
        riseTimer = 0f;
        StopAgent();
        MoveBodyToward(submergedLocalY, 999f);
        UpdateVisuals();
        ClearCameraEffect();
    }

    void MoveBodyByRisePercent()
    {
        float percent = riseDuration > 0f ? Mathf.Clamp01(riseTimer / riseDuration) : 1f;
        float y = Mathf.Lerp(submergedLocalY, emergedLocalY, percent);
        SetBodyLocalY(y);
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
        bool submerged = state == VictoriaRegiaState.Submerged;
        bool visibleBody = state != VictoriaRegiaState.Submerged;

        if (submergedVisualRoot != null)
            submergedVisualRoot.SetActive(submerged);

        if (glowingEyesRoot != null)
            glowingEyesRoot.SetActive(submerged || state == VictoriaRegiaState.Rising);

        if (emergedVisualRoot != null)
            emergedVisualRoot.SetActive(visibleBody);
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
        SetAgentStopped(true);
    }

    void SetAgentStopped(bool stopped)
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = stopped;
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindObjectOfType<PlayerVignetteEffect>();
    }

    void UpdateCameraEffect(float intensity)
    {
        if (cameraEffects == null) return;
        cameraEffects.SetThreatIntensity(intensity);
    }

    void ClearCameraEffect()
    {
        if (cameraEffects != null)
            cameraEffects.ClearThreatIntensity();
    }

    void OnDestroy()
    {
        RestoreVictimControls(targetStatus);
        ClearCameraEffect();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, gazeCheckDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, grappleRange);
    }
}
