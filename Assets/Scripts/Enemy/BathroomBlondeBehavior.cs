using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

public class BathroomBlondeBehavior : MonoBehaviour
{
    enum BathroomBlondeState
    {
        Hidden,
        EmergingFromMirror,
        HoldingVictim,
        Retreating
    }

    [Header("Hazards")]
    public BathroomBlondeMirror mirrorPrefab;
    public BathroomBlondeDrain drainPrefab;
    public Transform[] hazardSpawnPoints;
    public bool useWaterSourcesAsSpawnAnchors = true;
    public float mirrorSpawnInterval = 18f;
    public float drainSpawnInterval = 12f;
    public int maxActiveMirrors = 3;
    public int maxActiveDrains = 4;
    public float spawnRadius = 9f;
    public float spawnNavMeshSampleRadius = 4f;

    [Header("Manifestation")]
    public GameObject hiddenVisualRoot;
    public GameObject emergingVisualRoot;
    public Transform bodyRoot;
    public float hiddenLocalY = -1.5f;
    public float emergedLocalY = 0f;
    public float emergeDuration = 5f;
    public float retreatDuration = 1.5f;
    public float riseSpeed = 2.5f;
    public bool makeRigidbodyKinematic = true;

    [Header("Hair Hold")]
    public Transform hairHoldPoint;
    public Vector3 hairHoldOffset = new Vector3(0f, 1.1f, 0.55f);
    public float hairHoldDuration = 4f;
    public int escapeClicksRequired = 18;
    public bool destroyMirrorAfterEscape = true;
    public bool destroyMirrorAfterSwallow = true;

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float mirrorThreatVignette = 0.75f;
    [Range(0f, 1f)] public float holdThreatVignette = 0.9f;
    public float emergeShakeAmplitude = 0.8f;
    public float emergeShakeFrequency = 10f;
    public float emergeShakeDuration = 0.35f;
    public float holdShakeAmplitude = 1f;
    public float holdShakeFrequency = 13f;
    public float holdShakeDuration = 0.35f;

    private BathroomBlondeState state = BathroomBlondeState.Hidden;
    private BathroomBlondeMirror activeMirror;
    private PlayerStatus activeVictim;
    private Transform activeVictimTransform;
    private PlayerMovement heldMovement;
    private PlayerInventory heldInventory;
    private WaterCannon heldWaterCannon;
    private Rigidbody heldRigidbody;
    private bool heldMovementWasEnabled;
    private bool heldInventoryWasEnabled;
    private bool heldWaterCannonWasEnabled;
    private bool heldRigidbodyWasKinematic;
    private bool heldRigidbodyUsedGravity;
    private float mirrorSpawnTimer;
    private float drainSpawnTimer;
    private float stateTimer;
    private int escapeClicks;
    private Vector3 hiddenPosition;
    private PlayerMovement effectTargetMovement;

    void Start()
    {
        hiddenPosition = transform.position;
        ConfigureOwnPhysics();
        mirrorSpawnTimer = Random.Range(2f, Mathf.Max(2f, mirrorSpawnInterval));
        drainSpawnTimer = Random.Range(2f, Mathf.Max(2f, drainSpawnInterval));
        ResolveCameraEffects();
        EnterHidden(true);
    }

    void Update()
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        ResolveCameraEffects();
        UpdateHazardSpawning();

        switch (state)
        {
            case BathroomBlondeState.Hidden:
                UpdateHidden();
                break;
            case BathroomBlondeState.EmergingFromMirror:
                UpdateEmerging();
                break;
            case BathroomBlondeState.HoldingVictim:
                UpdateHoldingVictim();
                break;
            case BathroomBlondeState.Retreating:
                UpdateRetreating();
                break;
        }
    }

    void ConfigureOwnPhysics()
    {
        if (!makeRigidbodyKinematic) return;

        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null) return;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;
    }

    void UpdateHazardSpawning()
    {
        mirrorSpawnTimer -= Time.deltaTime;
        drainSpawnTimer -= Time.deltaTime;

        if (mirrorSpawnTimer <= 0f)
        {
            mirrorSpawnTimer = mirrorSpawnInterval;
            TrySpawnMirror();
        }

        if (drainSpawnTimer <= 0f)
        {
            drainSpawnTimer = drainSpawnInterval;
            TrySpawnDrain();
        }
    }

    void UpdateHidden()
    {
        MoveBodyToward(hiddenLocalY, riseSpeed);
        ClearCameraEffect();
    }

    void UpdateEmerging()
    {
        if (activeMirror == null || activeVictim == null || activeVictim.IsDead())
        {
            BeginRetreat();
            return;
        }

        FollowActiveMirror();
        FaceTarget(activeVictim.transform.position);
        UpdateCameraEffect(mirrorThreatVignette);

        stateTimer -= Time.deltaTime;
        float percent = emergeDuration > 0f ? 1f - Mathf.Clamp01(stateTimer / emergeDuration) : 1f;
        SetBodyLocalY(Mathf.Lerp(hiddenLocalY, emergedLocalY, percent));

        if (stateTimer <= 0f)
            BeginHairHold();
    }

    void BeginHairHold()
    {
        if (activeVictim == null || activeVictim.IsDead())
        {
            BeginRetreat();
            return;
        }

        state = BathroomBlondeState.HoldingVictim;
        stateTimer = hairHoldDuration;
        escapeClicks = 0;
        activeVictimTransform = activeVictim.transform;
        StoreAndBlockVictimControls(activeVictim);
        activeMirror?.MarkBlondeOut();

        EnemyPlayerEffects.Pulse(
            ref effectTargetMovement,
            activeVictim,
            activeVictimTransform,
            cameraEffects,
            holdThreatVignette,
            holdShakeDuration,
            holdShakeAmplitude,
            holdShakeFrequency,
            holdShakeDuration);
    }

    void UpdateHoldingVictim()
    {
        if (activeVictim == null || activeVictimTransform == null || activeVictim.IsDead())
        {
            BeginRetreat();
            return;
        }

        FollowActiveMirror();
        SetBodyLocalY(emergedLocalY);
        FaceTarget(activeVictimTransform.position);
        HoldVictimWithHair();
        CountEscapeClicks();
        UpdateCameraEffect(holdThreatVignette);

        stateTimer -= Time.deltaTime;
        if (escapeClicks >= escapeClicksRequired)
        {
            EscapeHairHold();
            return;
        }

        if (stateTimer <= 0f)
            SwallowVictim();
    }

    void CountEscapeClicks()
    {
        if (Mouse.current == null) return;
        if (Mouse.current.leftButton.wasPressedThisFrame)
            escapeClicks++;
    }

    void EscapeHairHold()
    {
        RestoreVictimControls(activeVictim);
        ClearHeldReferences();

        if (destroyMirrorAfterEscape && activeMirror != null)
            activeMirror.DestroyAfterBlondeFinished();

        BeginRetreat();
    }

    void SwallowVictim()
    {
        PlayerStatus victim = activeVictim;
        RestoreVictimControls(victim);
        ClearHeldReferences();

        if (victim != null && !victim.IsDead())
            victim.Die();

        if (destroyMirrorAfterSwallow && activeMirror != null)
            activeMirror.DestroyAfterBlondeFinished();

        BeginRetreat();
    }

    void UpdateRetreating()
    {
        stateTimer -= Time.deltaTime;
        MoveBodyToward(hiddenLocalY, riseSpeed * 2f);
        UpdateCameraEffect(mirrorThreatVignette * 0.35f);

        if (stateTimer <= 0f)
            EnterHidden(false);
    }

    public void BeginMirrorEmergence(BathroomBlondeMirror mirror, PlayerStatus victim, Transform emergePoint)
    {
        if (mirror == null || victim == null || victim.IsDead()) return;
        if (state == BathroomBlondeState.HoldingVictim) return;

        activeMirror = mirror;
        activeVictim = victim;
        activeVictimTransform = victim.transform;
        state = BathroomBlondeState.EmergingFromMirror;
        stateTimer = emergeDuration;
        FollowActiveMirror();
        SetBodyLocalY(hiddenLocalY);
        UpdateVisuals();

        EnemyPlayerEffects.Pulse(
            ref effectTargetMovement,
            activeVictim,
            activeVictimTransform,
            cameraEffects,
            mirrorThreatVignette,
            emergeShakeDuration,
            emergeShakeAmplitude,
            emergeShakeFrequency,
            emergeShakeDuration);
    }

    public void CancelMirrorEmergence(BathroomBlondeMirror mirror)
    {
        if (mirror != activeMirror) return;
        if (state == BathroomBlondeState.HoldingVictim) return;
        BeginRetreat();
    }

    public void ReceiveWaterHit(Vector3 sourcePosition)
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        if (state == BathroomBlondeState.EmergingFromMirror)
            BeginRetreat();
    }

    void BeginRetreat()
    {
        RestoreVictimControls(activeVictim);
        ClearHeldReferences();
        activeMirror = null;
        activeVictim = null;
        activeVictimTransform = null;
        state = BathroomBlondeState.Retreating;
        stateTimer = retreatDuration;
        UpdateVisuals();
    }

    void EnterHidden(bool immediate)
    {
        RestoreVictimControls(activeVictim);
        ClearHeldReferences();
        activeMirror = null;
        activeVictim = null;
        activeVictimTransform = null;
        state = BathroomBlondeState.Hidden;
        transform.position = hiddenPosition;

        if (immediate)
            SetBodyLocalY(hiddenLocalY);

        UpdateVisuals();
        ClearCameraEffect();
    }

    void FollowActiveMirror()
    {
        if (activeMirror == null) return;

        Transform emergePoint = activeMirror.GetEmergencePoint();
        Transform source = emergePoint != null ? emergePoint : activeMirror.transform;
        transform.SetPositionAndRotation(source.position, source.rotation);
    }

    void TrySpawnMirror()
    {
        if (mirrorPrefab == null) return;
        if (FindObjectsByType<BathroomBlondeMirror>(
                FindObjectsInactive.Exclude).Length >= maxActiveMirrors)
        {
            return;
        }

        Vector3 position;
        if (!TryChooseHazardPosition(out position)) return;

        BathroomBlondeMirror mirror = Instantiate(mirrorPrefab, position, Quaternion.identity);
        mirror.Initialize(this);
    }

    void TrySpawnDrain()
    {
        if (drainPrefab == null) return;
        if (FindObjectsByType<BathroomBlondeDrain>(
                FindObjectsInactive.Exclude).Length >= maxActiveDrains)
        {
            return;
        }

        Vector3 position;
        if (!TryChooseHazardPosition(out position)) return;

        BathroomBlondeDrain drain = Instantiate(drainPrefab, position, Quaternion.identity);
        drain.Initialize(this);
    }

    bool TryChooseHazardPosition(out Vector3 position)
    {
        position = transform.position;

        Transform explicitPoint = ChooseExplicitSpawnPoint();
        if (explicitPoint != null && TrySampleNavMesh(explicitPoint.position, spawnNavMeshSampleRadius, out position))
            return true;

        if (useWaterSourcesAsSpawnAnchors && TryChooseWaterSourcePosition(out position))
            return true;

        PlayerStatus player = FindClosestPlayer();
        Vector3 anchor = player != null ? player.transform.position : transform.position;
        Vector3 wanted = anchor + Random.insideUnitSphere * spawnRadius;
        wanted.y = anchor.y;
        return TrySampleNavMesh(wanted, spawnNavMeshSampleRadius, out position);
    }

    Transform ChooseExplicitSpawnPoint()
    {
        if (hazardSpawnPoints == null || hazardSpawnPoints.Length == 0)
            return null;

        int start = Random.Range(0, hazardSpawnPoints.Length);
        for (int i = 0; i < hazardSpawnPoints.Length; i++)
        {
            Transform point = hazardSpawnPoints[(start + i) % hazardSpawnPoints.Length];
            if (point != null)
                return point;
        }

        return null;
    }

    bool TryChooseWaterSourcePosition(out Vector3 position)
    {
        position = transform.position;
        WaterSourceDryable[] sources =
            FindObjectsByType<WaterSourceDryable>(FindObjectsInactive.Exclude);
        if (sources == null || sources.Length == 0) return false;

        int start = Random.Range(0, sources.Length);
        for (int i = 0; i < sources.Length; i++)
        {
            WaterSourceDryable source = sources[(start + i) % sources.Length];
            if (source == null || source.isDry) continue;

            Vector2 offset = Random.insideUnitCircle * spawnRadius;
            Vector3 wanted = source.transform.position + new Vector3(offset.x, 0f, offset.y);
            if (TrySampleNavMesh(wanted, spawnNavMeshSampleRadius, out position))
                return true;
        }

        return false;
    }

    PlayerStatus FindClosestPlayer()
    {
        PlayerStatus[] players =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        PlayerStatus best = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus player = players[i];
            if (player == null || player.IsDead()) continue;

            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance >= bestDistance) continue;

            best = player;
            bestDistance = distance;
        }

        return best;
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

    void StoreAndBlockVictimControls(PlayerStatus victim)
    {
        if (victim == null) return;

        victim.AddExternalControlLock();
        heldMovement = victim.GetComponent<PlayerMovement>();
        heldInventory = victim.GetComponent<PlayerInventory>();
        heldWaterCannon = victim.GetComponentInChildren<WaterCannon>();
        heldRigidbody = victim.GetComponent<Rigidbody>();

        if (heldMovement != null)
        {
            heldMovementWasEnabled = heldMovement.enabled;
            heldMovement.enabled = false;
        }

        if (heldInventory != null)
        {
            heldInventoryWasEnabled = heldInventory.enabled;
            heldInventory.enabled = false;
        }

        if (heldWaterCannon != null)
        {
            heldWaterCannonWasEnabled = heldWaterCannon.enabled;
            heldWaterCannon.enabled = false;
        }

        if (heldRigidbody != null)
        {
            heldRigidbodyWasKinematic = heldRigidbody.isKinematic;
            heldRigidbodyUsedGravity = heldRigidbody.useGravity;
            heldRigidbody.linearVelocity = Vector3.zero;
            heldRigidbody.angularVelocity = Vector3.zero;
            heldRigidbody.useGravity = false;
            heldRigidbody.isKinematic = true;
        }
    }

    void RestoreVictimControls(PlayerStatus victim)
    {
        if (victim != null)
            victim.RemoveExternalControlLock();

        bool canRestore = victim != null && victim.CanAct();

        if (heldMovement != null)
            heldMovement.enabled = canRestore && heldMovementWasEnabled;

        if (heldInventory != null)
            heldInventory.enabled = canRestore && heldInventoryWasEnabled;

        if (heldWaterCannon != null)
            heldWaterCannon.enabled = canRestore && heldWaterCannonWasEnabled;

        if (heldRigidbody != null && canRestore)
        {
            heldRigidbody.isKinematic = heldRigidbodyWasKinematic;
            heldRigidbody.useGravity = heldRigidbodyUsedGravity;
        }
    }

    void ClearHeldReferences()
    {
        heldMovement = null;
        heldInventory = null;
        heldWaterCannon = null;
        heldRigidbody = null;
        heldMovementWasEnabled = false;
        heldInventoryWasEnabled = false;
        heldWaterCannonWasEnabled = false;
    }

    void HoldVictimWithHair()
    {
        if (activeVictimTransform == null) return;

        Vector3 holdPosition = hairHoldPoint != null
            ? hairHoldPoint.position
            : transform.TransformPoint(hairHoldOffset);

        Rigidbody body = activeVictimTransform.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = holdPosition;
            body.rotation = transform.rotation;
            return;
        }

        activeVictimTransform.SetPositionAndRotation(holdPosition, transform.rotation);
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

    void FaceTarget(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f) return;

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(direction.normalized),
            Time.deltaTime * 8f);
    }

    void UpdateVisuals()
    {
        bool hidden = state == BathroomBlondeState.Hidden;
        bool visible = state == BathroomBlondeState.EmergingFromMirror ||
            state == BathroomBlondeState.HoldingVictim ||
            state == BathroomBlondeState.Retreating;

        if (hiddenVisualRoot != null)
            hiddenVisualRoot.SetActive(hidden);

        if (emergingVisualRoot != null)
            emergingVisualRoot.SetActive(visible);
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindAnyObjectByType<PlayerVignetteEffect>();
    }

    void UpdateCameraEffect(float intensity)
    {
        EnemyPlayerEffects.SetThreatIntensity(
            ref effectTargetMovement,
            activeVictim,
            activeVictimTransform,
            cameraEffects,
            intensity);
    }

    void ClearCameraEffect()
    {
        EnemyPlayerEffects.ClearThreat(ref effectTargetMovement, cameraEffects, true);
    }

    void OnDestroy()
    {
        RestoreVictimControls(activeVictim);
        ClearCameraEffect();
    }
}
