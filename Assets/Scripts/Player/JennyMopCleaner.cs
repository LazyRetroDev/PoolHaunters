using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class JennyMopCleaner : MonoBehaviour
{
    [Header("Input")]
    public string attackActionName = "Attack";
    public string abilityActionName = "Ability";
    public Key fallbackDashKey = Key.Q;
    public bool requireMovement = true;

    [Header("Sweep")]
    public Transform mopHead;
    public Vector3 mopLocalOffset = new Vector3(0f, 0.15f, 1f);
    public Vector3 mopHalfExtents = new Vector3(0.65f, 0.2f, 0.45f);
    public float cleanPowerPerSecond = 55f;
    public float poolCleanPowerPerSecond = 35f;
    public float cleanContactRadius = 0.5f;
    public LayerMask cleanMask = ~0;
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

    [Header("Water Usage")]
    public bool consumeWaterToClean = true;
    public float waterUsagePerSecond = 7.5f;
    public float contaminatedCleaningMultiplier = 0.25f;
    public float chemicallyEnhancedCleaningMultiplier = 1.35f;

    [Header("Contaminated Mop Trail")]
    public bool spreadContaminatedWaterOnCleanSurfaces = true;
    public float contaminatedTrailInterval = 0.08f;
    public float contaminatedTrailContactRadius = 0.28f;
    public int contaminatedTrailWidthSamples = 3;
    public int contaminatedTrailLengthSamples = 2;
    public float contaminatedTrailRayHeight = 0.45f;
    public float contaminatedTrailRayDistance = 1.2f;

    [Header("Surface Slide")]
    public bool snapMopToSurface = true;
    public LayerMask surfaceMask = ~0;
    public float surfaceRayHeight = 1.25f;
    public float surfaceRayDistance = 3f;
    public float surfaceOffset = 0.05f;

    [Header("Visual Placeholder")]
    public bool createPlaceholderMop = true;
    public bool hideMopWhenDisabled = true;
    public Vector3 placeholderSize = new Vector3(1.25f, 0.12f, 0.35f);
    public Color placeholderColor = new Color(0.65f, 0.85f, 1f, 1f);

    [Header("Surf Dash")]
    public bool enableSurfDash = true;
    public float dashSpeed = 13f;
    public float dashDuration = 0.45f;
    public float dashCooldown = 1.1f;
    public float dashStaminaCost = 18f;
    public float dashWaterCost = 4f;
    public float dashWaterUsagePerSecond = 8f;
    public float dashCleanMultiplier = 1.4f;
    public Vector3 dashMopHalfExtents = new Vector3(1.1f, 0.22f, 0.65f);
    public bool dashCleansWithoutAttack = true;
    public bool scaleMopVisualDuringDash = true;
    public float dashVisualScaleSpeed = 18f;

    [Header("Surf Dash Presentation")]
    public bool useDashMopOffset = true;
    public Vector3 dashMopLocalOffset = new Vector3(0f, 0.08f, 0.55f);
    public bool placeDashMopUnderFeet = true;
    public float dashSurfaceRayHeight = 2f;
    public float dashSurfaceRayDistance = 4f;
    public float dashMopSurfaceOffset = 0.04f;
    public float dashMopFallbackDrop = 1.1f;
    public bool faceDashDirection = true;
    public float dashTurnSpeed = 18f;
    public ParticleSystem dashTrailParticles;
    public bool autoFindDashTrailParticles = true;
    public bool stopTrailWhenDashEnds = true;

    private readonly HashSet<DirtSpot> dirtHits = new HashSet<DirtSpot>();
    private readonly HashSet<PoolCleaningZone> poolHits = new HashSet<PoolCleaningZone>();
    private Rigidbody rb;
    private PlayerInput playerInput;
    private PlayerMovement movement;
    private PlayerStatus playerStatus;
    private WaterCannon contaminationDirtSource;
    private PlayerPetrify petrify;
    private InputAction attackAction;
    private InputAction abilityAction;
    private InputAction moveAction;
    private Renderer placeholderRenderer;
    private Vector3 normalMopHeadScale = Vector3.one;
    private bool hasCachedMopScale;
    private float dashTimer;
    private float nextDashTime;
    private float nextContaminatedTrailTime;
    private bool dashTrailPlaying;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        playerInput = GetComponent<PlayerInput>();
        movement = GetComponent<PlayerMovement>();
        playerStatus = GetComponent<PlayerStatus>();
        contaminationDirtSource = GetComponentInChildren<WaterCannon>(true);
        petrify = GetComponent<PlayerPetrify>();

        if (playerInput != null)
        {
            attackAction = playerInput.actions.FindAction(attackActionName, false);
            abilityAction = playerInput.actions.FindAction(abilityActionName, false);
            moveAction = playerInput.actions.FindAction("Move", false);
        }

        if (mopHead == null)
            mopHead = CreateMopHead();

        if (dashTrailParticles == null && autoFindDashTrailParticles)
            dashTrailParticles = GetComponentInChildren<ParticleSystem>(true);

        CacheMopScale();
    }

    void Update()
    {
        UpdateMopPose();
        TryStartDash();

        if (!CanMop())
            return;

        SweepClean();
    }

    void FixedUpdate()
    {
        UpdateDashMovement();
    }

    void OnEnable()
    {
        if (mopHead != null)
            mopHead.gameObject.SetActive(true);
    }

    bool CanMop()
    {
        if (playerStatus != null && !playerStatus.CanAct())
            return false;

        if (petrify != null && petrify.IsPetrified())
            return false;

        if (movement != null && !movement.AcceptsInput)
            return false;

        if (attackAction == null || !attackAction.IsPressed())
        {
            if (dashCleansWithoutAttack && IsDashing())
                return true;

            return false;
        }

        if (!requireMovement || moveAction == null)
            return true;

        return moveAction.ReadValue<Vector2>().sqrMagnitude > 0.05f;
    }

    void OnDisable()
    {
        if (hideMopWhenDisabled && mopHead != null)
            mopHead.gameObject.SetActive(false);

        SetDashTrailPlaying(false);
    }

    void SweepClean()
    {
        float waterThisFrame = GetWaterUsageThisFrame();
        WaterQuality waterQuality = playerStatus != null
            ? playerStatus.GetWaterQuality()
            : WaterQuality.Clean;

        if (consumeWaterToClean && (playerStatus == null || !playerStatus.ConsumeWater(waterThisFrame)))
            return;

        float cleanMultiplier = GetCleaningMultiplier(waterQuality);
        if (IsDashing())
            cleanMultiplier *= dashCleanMultiplier;

        dirtHits.Clear();
        poolHits.Clear();

        Vector3 center = GetMopWorldPosition();
        Quaternion rotation = GetMopWorldRotation();
        Vector3 halfExtents = IsDashing() ? dashMopHalfExtents : mopHalfExtents;

        if (waterQuality == WaterQuality.Contaminated)
            StampContaminatedMopTrail(center, rotation, halfExtents, waterThisFrame);

        Collider[] hits = Physics.OverlapBox(
            center,
            halfExtents,
            rotation,
            cleanMask,
            triggerInteraction);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null || hit.transform.IsChildOf(transform))
                continue;

            Vector3 contactPoint = hit.ClosestPoint(center);
            if (!IsFinite(contactPoint))
                contactPoint = center;

            DirtSpot dirt = hit.GetComponentInParent<DirtSpot>();
            if (dirt != null && !dirtHits.Contains(dirt))
            {
                dirtHits.Add(dirt);
                if (waterQuality == WaterQuality.Contaminated)
                {
                    dirt.ApplyContaminatedWaterAtWorldPoint(
                        contactPoint,
                        cleanContactRadius,
                        waterThisFrame);
                }
                else
                {
                    dirt.CleanAtWorldPoint(
                        contactPoint,
                        cleanContactRadius,
                        cleanPowerPerSecond * cleanMultiplier * Time.deltaTime);
                }
            }

            PoolCleaningZone pool = hit.GetComponentInParent<PoolCleaningZone>();
            if (pool != null && !poolHits.Contains(pool))
            {
                poolHits.Add(pool);
                pool.ApplyWaterAtWorldPoint(
                    contactPoint,
                    cleanContactRadius,
                    poolCleanPowerPerSecond * cleanMultiplier * Time.deltaTime,
                    waterThisFrame,
                    waterQuality);
            }
        }
    }

    void StampContaminatedMopTrail(
        Vector3 center,
        Quaternion rotation,
        Vector3 halfExtents,
        float waterAmount)
    {
        if (!spreadContaminatedWaterOnCleanSurfaces || waterAmount <= 0f)
            return;

        if (Time.time < nextContaminatedTrailTime)
            return;

        nextContaminatedTrailTime =
            Time.time + Mathf.Max(0.01f, contaminatedTrailInterval);

        int widthSamples = Mathf.Max(1, contaminatedTrailWidthSamples);
        int lengthSamples = Mathf.Max(1, contaminatedTrailLengthSamples);
        int totalSamples = widthSamples * lengthSamples;
        float waterPerSample = waterAmount / totalSamples;

        Vector3 right = rotation * Vector3.right;
        Vector3 forward = rotation * Vector3.forward;

        for (int x = 0; x < widthSamples; x++)
        {
            float widthT = widthSamples == 1
                ? 0f
                : Mathf.Lerp(-1f, 1f, x / (float)(widthSamples - 1));

            for (int z = 0; z < lengthSamples; z++)
            {
                float lengthT = lengthSamples == 1
                    ? 0f
                    : Mathf.Lerp(-1f, 1f, z / (float)(lengthSamples - 1));

                Vector3 sampleCenter =
                    center +
                    right * (widthT * halfExtents.x) +
                    forward * (lengthT * halfExtents.z);

                if (TryGetContaminationSurface(
                    sampleCenter,
                    out Vector3 contactPoint,
                    out Vector3 surfaceNormal))
                {
                    CreateOrGrowContaminatedDirt(
                        contactPoint,
                        surfaceNormal,
                        waterPerSample);
                }
            }
        }
    }

    bool TryGetContaminationSurface(
        Vector3 sampleCenter,
        out Vector3 contactPoint,
        out Vector3 surfaceNormal)
    {
        Vector3 rayOrigin = sampleCenter + Vector3.up * contaminatedTrailRayHeight;
        if (Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out RaycastHit hit,
            contaminatedTrailRayDistance,
            cleanMask,
            QueryTriggerInteraction.Ignore))
        {
            if (IsValidContaminationSurface(hit.collider))
            {
                contactPoint = hit.point;
                surfaceNormal = hit.normal;
                return true;
            }
        }

        contactPoint = Vector3.zero;
        surfaceNormal = Vector3.up;
        return false;
    }

    bool IsValidContaminationSurface(Collider collider)
    {
        if (collider == null || collider.isTrigger)
            return false;

        if (collider.transform.IsChildOf(transform))
            return false;

        if (collider.GetComponentInParent<PlayerStatus>() != null)
            return false;

        return true;
    }

    void CreateOrGrowContaminatedDirt(
        Vector3 contactPoint,
        Vector3 surfaceNormal,
        float waterAmount)
    {
        if (contaminationDirtSource == null)
            contaminationDirtSource = GetComponentInChildren<WaterCannon>(true);

        float contactRadius = Mathf.Max(0.01f, contaminatedTrailContactRadius);

        if (ShouldRequestServerContaminatedDirt())
        {
            movement.RequestCreateOrGrowContaminatedDirt(
                contactPoint,
                surfaceNormal,
                contactRadius,
                waterAmount);
            return;
        }

        if (contaminationDirtSource != null)
        {
            contaminationDirtSource.CreateOrGrowContaminatedDirtFromNetwork(
                contactPoint,
                surfaceNormal,
                contactRadius,
                waterAmount);
        }
    }

    bool ShouldRequestServerContaminatedDirt()
    {
        return movement != null &&
            movement.IsSpawned &&
            !movement.IsServer;
    }

    void TryStartDash()
    {
        if (!enableSurfDash || Time.time < nextDashTime || IsDashing())
            return;

        if (!DashPressedThisFrame())
            return;

        if (!CanUseDashResources())
            return;

        if (movement != null && !movement.ConsumeStamina(dashStaminaCost))
            return;

        if (playerStatus != null && !playerStatus.ConsumeWater(dashWaterCost))
            return;

        dashTimer = dashDuration;
        nextDashTime = Time.time + dashCooldown;
        SetDashTrailPlaying(true);
    }

    bool DashPressedThisFrame()
    {
        if (abilityAction != null && abilityAction.WasPressedThisFrame())
            return true;

        return Keyboard.current != null &&
            fallbackDashKey != Key.None &&
            Keyboard.current[fallbackDashKey].wasPressedThisFrame;
    }

    bool CanUseDashResources()
    {
        if (playerStatus != null && !playerStatus.CanAct())
            return false;

        if (movement != null && !movement.AcceptsInput)
            return false;

        if (movement != null && !movement.HasStamina(dashStaminaCost))
            return false;

        return playerStatus == null || playerStatus.GetCurrentWater() >= dashWaterCost;
    }

    void UpdateDashMovement()
    {
        if (!IsDashing())
        {
            SetDashTrailPlaying(false);
            return;
        }

        dashTimer = Mathf.Max(0f, dashTimer - Time.fixedDeltaTime);

        Vector3 direction = GetDashDirection();
        Vector3 delta = direction * dashSpeed * Time.fixedDeltaTime;

        if (rb != null)
            rb.MovePosition(rb.position + delta);
        else
            transform.position += delta;

        TurnTowardDashDirection(direction);

        if (!IsDashing())
            SetDashTrailPlaying(false);
    }

    void TurnTowardDashDirection(Vector3 direction)
    {
        if (!faceDashDirection || direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        float blend = 1f - Mathf.Exp(-dashTurnSpeed * Time.fixedDeltaTime);
        Quaternion nextRotation = Quaternion.Slerp(transform.rotation, targetRotation, blend);

        if (rb != null)
            rb.MoveRotation(nextRotation);
        else
            transform.rotation = nextRotation;
    }

    Vector3 GetDashDirection()
    {
        Vector3 direction = transform.forward;

        if (moveAction != null)
        {
            Vector2 move = moveAction.ReadValue<Vector2>();
            if (move.sqrMagnitude > 0.05f && Camera.main != null)
            {
                Vector3 camForward = Camera.main.transform.forward;
                Vector3 camRight = Camera.main.transform.right;
                camForward.y = 0f;
                camRight.y = 0f;
                camForward.Normalize();
                camRight.Normalize();
                direction = camForward * move.y + camRight * move.x;
            }
        }

        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f)
            direction = transform.forward;

        return direction.normalized;
    }

    bool IsDashing()
    {
        return dashTimer > 0f;
    }

    float GetWaterUsageThisFrame()
    {
        if (!consumeWaterToClean)
            return 0f;

        float usage = IsDashing() ? dashWaterUsagePerSecond : waterUsagePerSecond;
        return usage * Time.deltaTime;
    }

    float GetCleaningMultiplier(WaterQuality waterQuality)
    {
        switch (waterQuality)
        {
            case WaterQuality.Contaminated:
                return contaminatedCleaningMultiplier;
            case WaterQuality.ChemicallyEnhanced:
                return chemicallyEnhancedCleaningMultiplier;
            default:
                return 1f;
        }
    }

    void UpdateMopPose()
    {
        if (mopHead == null)
            return;

        mopHead.position = GetMopWorldPosition();
        mopHead.rotation = GetMopWorldRotation();
        UpdateMopVisualScale();
    }

    void UpdateMopVisualScale()
    {
        if (!scaleMopVisualDuringDash || mopHead == null)
            return;

        CacheMopScale();

        Vector3 targetScale = IsDashing()
            ? Vector3.Scale(normalMopHeadScale, GetDashVisualScale())
            : normalMopHeadScale;

        float blend = 1f - Mathf.Exp(-dashVisualScaleSpeed * Time.deltaTime);
        mopHead.localScale = Vector3.Lerp(mopHead.localScale, targetScale, blend);
    }

    Vector3 GetDashVisualScale()
    {
        return new Vector3(
            SafeRatio(dashMopHalfExtents.x, mopHalfExtents.x),
            SafeRatio(dashMopHalfExtents.y, mopHalfExtents.y),
            SafeRatio(dashMopHalfExtents.z, mopHalfExtents.z));
    }

    float SafeRatio(float value, float baseline)
    {
        if (Mathf.Abs(baseline) <= 0.001f)
            return 1f;

        return Mathf.Max(0.01f, value / baseline);
    }

    void CacheMopScale()
    {
        if (hasCachedMopScale || mopHead == null)
            return;

        normalMopHeadScale = mopHead.localScale;
        hasCachedMopScale = true;
    }

    Vector3 GetMopWorldPosition()
    {
        if (IsDashing() && useDashMopOffset && placeDashMopUnderFeet)
            return GetDashMopSurfPosition();

        Vector3 localOffset = IsDashing() && useDashMopOffset
            ? dashMopLocalOffset
            : mopLocalOffset;
        Vector3 basePosition = transform.TransformPoint(localOffset);
        if (!snapMopToSurface)
            return basePosition;

        Vector3 origin = basePosition + Vector3.up * surfaceRayHeight;
        if (Physics.Raycast(
            origin,
            Vector3.down,
            out RaycastHit hit,
            surfaceRayDistance,
            surfaceMask,
            QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * surfaceOffset;
        }

        return basePosition;
    }

    Vector3 GetDashMopSurfPosition()
    {
        Vector3 planarOffset = new Vector3(dashMopLocalOffset.x, 0f, dashMopLocalOffset.z);
        Vector3 basePosition = transform.position + transform.TransformDirection(planarOffset);

        if (!snapMopToSurface)
            return basePosition + Vector3.down * dashMopFallbackDrop;

        Vector3 origin = basePosition + Vector3.up * dashSurfaceRayHeight;
        if (Physics.Raycast(
            origin,
            Vector3.down,
            out RaycastHit hit,
            dashSurfaceRayHeight + dashSurfaceRayDistance,
            surfaceMask,
            QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * dashMopSurfaceOffset;
        }

        return basePosition + Vector3.down * dashMopFallbackDrop;
    }

    Quaternion GetMopWorldRotation()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.001f)
            forward = transform.forward;

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    void SetDashTrailPlaying(bool playing)
    {
        if (dashTrailParticles == null || dashTrailPlaying == playing)
            return;

        dashTrailPlaying = playing;

        if (playing)
        {
            if (!dashTrailParticles.gameObject.activeSelf)
                dashTrailParticles.gameObject.SetActive(true);

            if (!dashTrailParticles.isPlaying)
                dashTrailParticles.Play(true);
        }
        else if (stopTrailWhenDashEnds)
        {
            dashTrailParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    Transform CreateMopHead()
    {
        GameObject mopObject = new GameObject("JennyMopHead");
        mopObject.transform.SetParent(transform, false);
        mopObject.transform.localScale = Vector3.one;

        if (createPlaceholderMop)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "MopPlaceholder";
            visual.transform.SetParent(mopObject.transform, false);
            visual.transform.localScale = placeholderSize;

            Collider visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null)
                Destroy(visualCollider);

            placeholderRenderer = visual.GetComponent<Renderer>();
            if (placeholderRenderer != null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader != null)
                    placeholderRenderer.material = new Material(shader);

                placeholderRenderer.material.color = placeholderColor;
            }
        }

        return mopObject.transform;
    }

    bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.35f, 0.8f, 1f, 0.35f);
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(
            GetMopWorldPosition(),
            GetMopWorldRotation(),
            Vector3.one);
        Gizmos.DrawCube(Vector3.zero, (IsDashing() ? dashMopHalfExtents : mopHalfExtents) * 2f);
        Gizmos.matrix = previousMatrix;
    }
}
