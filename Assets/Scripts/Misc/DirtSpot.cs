using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;

public class DirtSpot : NetworkBehaviour
{
    static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
    static readonly int EdgeGlowId = Shader.PropertyToID("_EdgeGlow");
    static readonly int CleanPointCountId = Shader.PropertyToID("_CleanPointCount");
    static readonly int CleanPointsId = Shader.PropertyToID("_CleanPoints");

    const int MaxCleanPoints = 512;

    [Header("Dirt")]
    public float maxDirt = 100f;
    public float currentDirt = 100f;
    public bool destroyWhenClean = true;

    public event Action<DirtSpot> OnCleaned;
    public bool IsCleaned { get; private set; }

    [Header("Accuracy (Physical Area Check)")]
    public bool usePhysicalAreaCheck = true;
    [Range(0.1f, 1f)]
    public float cleanCompletionThreshold = 0.95f;
    public int gridResolution = 10;
    [SerializeField] private float currentCleanPercentage;

    [Header("Visual")]
    public Renderer targetRenderer;
    public Collider targetCollider;
    public bool shrinkWhileCleaning = true;
    public bool hideRendererWhenClean = true;
    public float minimumScaleMultiplier = 0.15f;
    public bool useDissolveShader = true;
    public bool useLocalizedCleaning = true;
    public float dissolveEdgeGlow = 0.6f;
    public float cleanPointMergeDistance = 0.08f;

    [Header("Cleaning Radius")]
    public bool keepCleaningRadiusWorldSized = true;
    [Min(0.01f)] public float maxLocalCleanRadius = 8f;

    [Header("Surface Adhesion")]
    public bool adhereToSurface = true;
    public LayerMask adhesionMask = ~0;
    public float adhesionRadius = 0.12f;
    public float surfaceOffset = 0.01f;
    public float fallAcceleration = 18f;
    public float maxFallSpeed = 10f;
    public bool followAdheredSurfacePosition = true;

    [Header("Pool Dirt Stability")]
    public bool disableAdhesionWhenInPoolObjective = true;
    public bool ignorePoolAndWaterVolumesForAdhesion = true;

    [Header("Contamination Growth")]
    public bool createdByContaminatedWater = false;
    public float contaminatedGrowthPerWaterChunk = 1f;
    public float contaminatedWaterPerGrowthChunk = 50f;
    [SerializeField] private float contaminatedWaterStored;

    private Vector3 initialLocalScale;
    private MaterialPropertyBlock propertyBlock;
    private readonly Vector4[] cleanPoints = new Vector4[MaxCleanPoints];
    private int cleanPointCount;
    private int nextCleanPointIndex;

    private Vector3 lastHitPoint;
    private float lastHitTime = -1f;
    private bool isFadingOut = false;
    private SwimmingPoolObjective poolObjective;

    private Vector3[] dirtNodes;
    private bool[] nodeIsClean;
    private int totalNodes;
    private int cleanedNodes;

    private readonly RaycastHit[] adhesionRayHits = new RaycastHit[16];
    private Transform adheredSurface;
    private Vector3 adheredLocalPosition;
    private Quaternion adheredLocalRotation = Quaternion.identity;
    private bool isAdheredToSurface;
    private float currentFallSpeed;

    void Awake()
    {
        if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
        if (targetCollider == null) targetCollider = GetComponent<Collider>();
        poolObjective = GetComponentInParent<SwimmingPoolObjective>();
        if (poolObjective != null && disableAdhesionWhenInPoolObjective)
        {
            adhereToSurface = false;
            followAdheredSurfacePosition = false;
        }

        initialLocalScale = transform.localScale;
        propertyBlock = new MaterialPropertyBlock();
        currentDirt = Mathf.Clamp(currentDirt, 0f, maxDirt);

        GenerateDirtNodes();
        UpdateVisualState();
    }

    void Start()
    {
        if (adhereToSurface)
            TryAttachToNearbySurface();
    }

    void Update()
    {
        UpdateAdheredSurfacePosition();
        UpdateSurfaceAdhesion();
    }

    void GenerateDirtNodes()
    {
        Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);

        MeshFilter mf = GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
            localBounds = mf.sharedMesh.bounds;
        else if (targetCollider != null && targetCollider is BoxCollider box)
            localBounds = new Bounds(box.center, box.size);

        List<Vector3> nodes = new List<Vector3>();

        Vector3 scale = transform.lossyScale;

        int xRes = Mathf.Abs(scale.x * localBounds.size.x) > 0.05f ? gridResolution : 0;
        int yRes = Mathf.Abs(scale.y * localBounds.size.y) > 0.05f ? gridResolution : 0;
        int zRes = Mathf.Abs(scale.z * localBounds.size.z) > 0.05f ? gridResolution : 0;

        for (int x = 0; x <= xRes; x++)
        {
            for (int y = 0; y <= yRes; y++)
            {
                for (int z = 0; z <= zRes; z++)
                {
                    bool is3D = (xRes > 0 && yRes > 0 && zRes > 0);
                    if (is3D)
                    {
                        bool isSurface = (x == 0 || x == xRes || y == 0 || y == yRes || z == 0 || z == zRes);
                        if (!isSurface) continue;
                    }

                    float tx = xRes > 0 ? (float)x / xRes : 0.5f;
                    float ty = yRes > 0 ? (float)y / yRes : 0.5f;
                    float tz = zRes > 0 ? (float)z / zRes : 0.5f;

                    Vector3 localPos = new Vector3(
                        Mathf.Lerp(localBounds.min.x, localBounds.max.x, tx),
                        Mathf.Lerp(localBounds.min.y, localBounds.max.y, ty),
                        Mathf.Lerp(localBounds.min.z, localBounds.max.z, tz)
                    );
                    nodes.Add(localPos);
                }
            }
        }

        dirtNodes = nodes.ToArray();
        nodeIsClean = new bool[dirtNodes.Length];
        totalNodes = dirtNodes.Length;
        cleanedNodes = 0;
    }

    void UpdateSurfaceAdhesion()
    {
        if (!adhereToSurface || isAdheredToSurface || isFadingOut) return;
        if (TryAttachToNearbySurface()) return;

        currentFallSpeed = Mathf.Min(maxFallSpeed, currentFallSpeed + fallAcceleration * Time.deltaTime);
        float fallDistance = currentFallSpeed * Time.deltaTime;

        if (TryFindSurfaceBelow(adhesionRadius + fallDistance, out RaycastHit hit))
        {
            AttachToSurface(hit.collider, hit.point, hit.normal);
            return;
        }

        transform.position += Vector3.down * fallDistance;
    }

    void UpdateAdheredSurfacePosition()
    {
        if (!isAdheredToSurface) return;

        if (adheredSurface == null)
        {
            isAdheredToSurface = false;
            currentFallSpeed = 0f;
            return;
        }

        if (!followAdheredSurfacePosition) return;

        transform.SetPositionAndRotation(
            adheredSurface.TransformPoint(adheredLocalPosition),
            adheredSurface.rotation * adheredLocalRotation);
    }

    bool TryAttachToNearbySurface()
    {
        if (TryFindSurfaceAround(out Collider surfaceCollider, out Vector3 surfacePoint, out Vector3 surfaceNormal))
        {
            AttachToSurface(surfaceCollider, surfacePoint, surfaceNormal);
            return true;
        }

        if (TryFindSurfaceBelow(adhesionRadius, out RaycastHit hit))
        {
            AttachToSurface(hit.collider, hit.point, hit.normal);
            return true;
        }

        return false;
    }

    bool TryFindSurfaceAround(out Collider surfaceCollider, out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        surfaceCollider = null;
        surfacePoint = transform.position;
        surfaceNormal = Vector3.up;

        float bestSqrDistance = float.MaxValue;
        TryCastForSurface(Vector3.down, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        TryCastForSurface(Vector3.up, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        TryCastForSurface(Vector3.left, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        TryCastForSurface(Vector3.right, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        TryCastForSurface(Vector3.forward, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        TryCastForSurface(Vector3.back, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        TryCastForSurface(transform.forward, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        TryCastForSurface(-transform.forward, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        TryCastForSurface(transform.up, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        TryCastForSurface(-transform.up, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        TryCastForSurface(transform.right, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        TryCastForSurface(-transform.right, ref surfaceCollider, ref surfacePoint, ref surfaceNormal, ref bestSqrDistance);
        return surfaceCollider != null;
    }

    void TryCastForSurface(Vector3 direction, ref Collider surfaceCollider, ref Vector3 surfacePoint, ref Vector3 surfaceNormal, ref float bestSqrDistance)
    {
        if (direction.sqrMagnitude <= 0.0001f) return;

        float radius = Mathf.Max(0.01f, adhesionRadius);
        float backoff = Mathf.Min(Mathf.Max(0.01f, surfaceOffset + 0.02f), radius);
        Vector3 normalizedDirection = direction.normalized;
        Vector3 origin = transform.position - normalizedDirection * backoff;
        int hitCount = Physics.RaycastNonAlloc(origin, normalizedDirection, adhesionRayHits, radius + backoff, adhesionMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = adhesionRayHits[i];
            adhesionRayHits[i] = default;
            if (!IsValidAdhesionCollider(hit.collider)) continue;

            float sqrDistance = (hit.point - transform.position).sqrMagnitude;
            if (sqrDistance > radius * radius || sqrDistance >= bestSqrDistance) continue;

            surfaceCollider = hit.collider;
            surfacePoint = hit.point;
            surfaceNormal = hit.normal;
            bestSqrDistance = sqrDistance;
        }
    }

    bool TryFindSurfaceBelow(float maxDistance, out RaycastHit bestHit)
    {
        bestHit = default;

        float liftedStart = Mathf.Min(Mathf.Max(0.01f, adhesionRadius * 0.25f), 0.08f);
        Vector3 origin = transform.position + Vector3.up * liftedStart;
        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, adhesionRayHits, Mathf.Max(0.01f, maxDistance + liftedStart), adhesionMask, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = adhesionRayHits[i];
            adhesionRayHits[i] = default;
            if (!IsValidAdhesionCollider(hit.collider)) continue;
            if ((hit.point - transform.position).sqrMagnitude > maxDistance * maxDistance) continue;
            if (hit.distance >= bestDistance) continue;

            bestHit = hit;
            bestDistance = hit.distance;
            found = true;
        }

        return found;
    }

    bool IsValidAdhesionCollider(Collider candidate)
    {
        if (candidate == null || !candidate.enabled) return false;
        if (candidate.transform == transform || candidate.transform.IsChildOf(transform)) return false;
        if (targetCollider != null && candidate == targetCollider) return false;
        if (candidate.CompareTag("Player")) return false;
        if (candidate.GetComponentInParent<PlayerStatus>() != null) return false;
        if (candidate.GetComponentInParent<DirtSpot>() != null) return false;
        if (ignorePoolAndWaterVolumesForAdhesion &&
            (candidate.GetComponentInParent<PoolCleaningZone>() != null ||
             candidate.GetComponentInParent<WaterSourceDryable>() != null ||
             candidate.GetComponentInParent<WaterZone>() != null))
        {
            return false;
        }
        return true;
    }

    void AttachToSurface(Collider surfaceCollider, Vector3 surfacePoint, Vector3 surfaceNormal)
    {
        if (!IsValidAdhesionCollider(surfaceCollider)) return;

        surfaceNormal = surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;
        transform.position = surfacePoint + surfaceNormal * Mathf.Max(0f, surfaceOffset);
        transform.rotation = Quaternion.LookRotation(-surfaceNormal, GetSurfaceUp(surfaceNormal));

        adheredSurface = surfaceCollider.transform;
        adheredLocalPosition = adheredSurface.InverseTransformPoint(transform.position);
        adheredLocalRotation = Quaternion.Inverse(adheredSurface.rotation) * transform.rotation;
        isAdheredToSurface = true;
        currentFallSpeed = 0f;
        initialLocalScale = transform.localScale;
        GenerateDirtNodes();
        UpdateVisualState();
    }

    Vector3 GetSurfaceUp(Vector3 surfaceNormal)
    {
        Vector3 surfaceUp = Vector3.ProjectOnPlane(Vector3.up, surfaceNormal);
        if (surfaceUp.sqrMagnitude > 0.0001f)
            return surfaceUp.normalized;

        surfaceUp = Vector3.ProjectOnPlane(transform.up, surfaceNormal);
        if (surfaceUp.sqrMagnitude > 0.0001f)
            return surfaceUp.normalized;

        return Vector3.forward;
    }

    public bool IsPartiallyCleaned()
    {
        return currentDirt < maxDirt - 0.001f || currentCleanPercentage > 0.001f || cleanPointCount > 0;
    }

    public void ConfigureGeneratedContaminatedSpot(float initialSize, float growthPerWaterChunk, float waterPerGrowthChunk)
    {
        ConfigureGeneratedContaminatedSpotLocal(
            initialSize,
            growthPerWaterChunk,
            waterPerGrowthChunk);

        if (ShouldBroadcastNetworkState())
        {
            ConfigureGeneratedContaminatedSpotClientRpc(
                initialSize,
                growthPerWaterChunk,
                waterPerGrowthChunk);
        }
    }

    void ConfigureGeneratedContaminatedSpotLocal(float initialSize, float growthPerWaterChunk, float waterPerGrowthChunk)
    {
        createdByContaminatedWater = true;
        contaminatedGrowthPerWaterChunk = Mathf.Max(0f, growthPerWaterChunk);
        contaminatedWaterPerGrowthChunk = Mathf.Max(0.01f, waterPerGrowthChunk);
        contaminatedWaterStored = 0f;

        ApplySurfaceScale(Mathf.Max(0.01f, initialSize));
        ResetDirtyState();
        UpdateVisualState();
    }

    public void Clean(float amount)
    {
        if (IsPoolCleaningLocked())
            return;

        if (ShouldRequestServerStateChange())
        {
            CleanServerRpc(amount);
            return;
        }

        ApplyCleanLocal(amount);

        if (ShouldBroadcastNetworkState())
            CleanClientRpc(amount);
    }

    void ApplyCleanLocal(float amount)
    {
        if (amount <= 0f || currentDirt <= 0f || isFadingOut) return;

        currentDirt -= amount;
        currentDirt = Mathf.Clamp(currentDirt, 0f, maxDirt);

        UpdateVisualState();

        if (currentDirt <= 0f && !usePhysicalAreaCheck)
        {
            MarkCleaned();
            StartCoroutine(FadeOutAndDestroy());
        }
    }

    public void CleanAtWorldPoint(Vector3 worldPoint, float worldRadius, float amount)
    {
        CleanAtWorldPoint(worldPoint, worldRadius, amount, null);
    }

    public void CleanAtWorldPoint(
        Vector3 worldPoint,
        float worldRadius,
        float amount,
        PlayerStatus cleaner)
    {
        if (IsPoolCleaningLocked())
            return;

        if (ShouldRequestServerStateChange())
        {
            CleanAtWorldPointServerRpc(worldPoint, worldRadius, amount);
            return;
        }

        float previousDirtPercent = GetDirtPercent();
        CleanAtWorldPointLocal(worldPoint, worldRadius, amount);
        float cleanedFraction = Mathf.Max(0f, previousDirtPercent - GetDirtPercent());
        LevelRewardTracker.RecordCleaning(cleaner, cleanedFraction);

        if (ShouldBroadcastNetworkState())
            CleanAtWorldPointClientRpc(worldPoint, worldRadius, amount);

        NotifyPoolCleanAtWorldPoint(worldPoint, worldRadius, amount);
    }

    public void ApplySynchronizedPoolCleanAtWorldPoint(
        Vector3 worldPoint,
        float worldRadius,
        float amount)
    {
        CleanAtWorldPointLocal(worldPoint, worldRadius, amount);
    }

    void CleanAtWorldPointLocal(Vector3 worldPoint, float worldRadius, float amount)
    {
        if (amount <= 0f || currentDirt <= 0f || isFadingOut) return;

        bool areaCleaned = false;

        if (Time.time - lastHitTime < 0.15f)
        {
            float dist = Vector3.Distance(lastHitPoint, worldPoint);
            float step = worldRadius * 0.5f;

            if (dist > step)
            {
                int steps = Mathf.CeilToInt(dist / step);
                for (int i = 1; i < steps; i++)
                {
                    Vector3 interpPoint = Vector3.Lerp(lastHitPoint, worldPoint, (float)i / steps);
                    if (AddCleanPoint(interpPoint, worldRadius)) areaCleaned = true;
                }
            }
        }

        if (AddCleanPoint(worldPoint, worldRadius)) areaCleaned = true;

        lastHitPoint = worldPoint;
        lastHitTime = Time.time;

        if (poolObjective == null)
            poolObjective = GetComponentInParent<SwimmingPoolObjective>();
        if (poolObjective != null)
            poolObjective.NotifyActivelyCleaned();

        if (areaCleaned) ApplyCleanLocal(amount);
        else UpdateVisualState();
    }

    public void ApplyContaminatedWaterAtWorldPoint(Vector3 worldPoint, float worldRadius, float waterAmount)
    {
        if (ShouldRequestServerStateChange())
        {
            ApplyContaminatedWaterAtWorldPointServerRpc(
                worldPoint,
                worldRadius,
                waterAmount);
            return;
        }

        ApplyContaminatedWaterAtWorldPointLocal(
            worldPoint,
            worldRadius,
            waterAmount);

        if (ShouldBroadcastNetworkState())
        {
            ApplyContaminatedWaterAtWorldPointClientRpc(
                worldPoint,
                worldRadius,
                waterAmount);
        }
    }

    void ApplyContaminatedWaterAtWorldPointLocal(Vector3 worldPoint, float worldRadius, float waterAmount)
    {
        if (waterAmount <= 0f || isFadingOut) return;

        bool changed = RestoreAtWorldPoint(worldPoint, worldRadius, waterAmount);
        if (createdByContaminatedWater)
            changed |= ApplyContaminatedGrowth(waterAmount);

        if (changed)
            UpdateVisualState();
    }

    bool ShouldRequestServerStateChange()
    {
        return IsSpawned && !IsServer && IsNetworkSessionRunning();
    }

    bool ShouldBroadcastNetworkState()
    {
        return IsSpawned && IsServer && IsNetworkSessionRunning();
    }

    static bool IsNetworkSessionRunning()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening;
    }

    void NotifyPoolCleanAtWorldPoint(
        Vector3 worldPoint,
        float worldRadius,
        float amount)
    {
        if (poolObjective == null)
            poolObjective = GetComponentInParent<SwimmingPoolObjective>();

        if (poolObjective == null ||
            poolObjective.IsApplyingSynchronizedState ||
            !IsNetworkSessionRunning())
        {
            return;
        }

        if (IsSpawned)
            return;

        if (LevelObjectiveManager.Instance != null)
        {
            LevelObjectiveManager.Instance.NotifyPoolDirtSpotCleaned(
                poolObjective,
                this,
                worldPoint,
                worldRadius,
                amount);
        }
    }

    bool IsPoolCleaningLocked()
    {
        if (poolObjective == null)
            poolObjective = GetComponentInParent<SwimmingPoolObjective>();

        return poolObjective != null && poolObjective.IsCleaningLocked;
    }

    static bool IsFiniteVector3(Vector3 value)
    {
        return IsFiniteFloat(value.x) &&
            IsFiniteFloat(value.y) &&
            IsFiniteFloat(value.z);
    }

    static bool IsFiniteFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    static bool HasValidAmount(float value)
    {
        return IsFiniteFloat(value) && value > 0f;
    }

    [ServerRpc(RequireOwnership = false)]
    void CleanServerRpc(float amount)
    {
        if (!HasValidAmount(amount))
            return;
        if (IsPoolCleaningLocked())
            return;

        ApplyCleanLocal(amount);
        CleanClientRpc(amount);
    }

    [ServerRpc(RequireOwnership = false)]
    void CleanAtWorldPointServerRpc(
        Vector3 worldPoint,
        float worldRadius,
        float amount,
        ServerRpcParams rpcParams = default)
    {
        if (!IsFiniteVector3(worldPoint) ||
            !HasValidAmount(worldRadius) ||
            !HasValidAmount(amount))
        {
            return;
        }
        if (IsPoolCleaningLocked())
            return;

        float previousDirtPercent = GetDirtPercent();
        CleanAtWorldPointLocal(worldPoint, worldRadius, amount);
        float cleanedFraction = Mathf.Max(0f, previousDirtPercent - GetDirtPercent());
        LevelRewardTracker.RecordCleaningByClientId(
            rpcParams.Receive.SenderClientId,
            cleanedFraction);

        CleanAtWorldPointClientRpc(worldPoint, worldRadius, amount);
    }

    [ServerRpc(RequireOwnership = false)]
    void ApplyContaminatedWaterAtWorldPointServerRpc(
        Vector3 worldPoint,
        float worldRadius,
        float waterAmount)
    {
        if (!IsFiniteVector3(worldPoint) ||
            !HasValidAmount(worldRadius) ||
            !HasValidAmount(waterAmount))
        {
            return;
        }

        ApplyContaminatedWaterAtWorldPointLocal(
            worldPoint,
            worldRadius,
            waterAmount);
        ApplyContaminatedWaterAtWorldPointClientRpc(
            worldPoint,
            worldRadius,
            waterAmount);
    }

    [ClientRpc]
    void ConfigureGeneratedContaminatedSpotClientRpc(
        float initialSize,
        float growthPerWaterChunk,
        float waterPerGrowthChunk)
    {
        if (IsServer)
            return;

        ConfigureGeneratedContaminatedSpotLocal(
            initialSize,
            growthPerWaterChunk,
            waterPerGrowthChunk);
    }

    [ClientRpc]
    void CleanClientRpc(float amount)
    {
        if (IsServer)
            return;

        ApplyCleanLocal(amount);
    }

    [ClientRpc]
    void CleanAtWorldPointClientRpc(
        Vector3 worldPoint,
        float worldRadius,
        float amount)
    {
        if (IsServer)
            return;

        CleanAtWorldPointLocal(worldPoint, worldRadius, amount);
    }

    [ClientRpc]
    void ApplyContaminatedWaterAtWorldPointClientRpc(
        Vector3 worldPoint,
        float worldRadius,
        float waterAmount)
    {
        if (IsServer)
            return;

        ApplyContaminatedWaterAtWorldPointLocal(
            worldPoint,
            worldRadius,
            waterAmount);
    }

    public float GetDirtPercent()
    {
        return maxDirt > 0f ? currentDirt / maxDirt : 0f;
    }

    public void ForceClean()
    {
        if (IsCleaned)
            return;

        MarkCleaned();
        UpdateVisualState();

        if (hideRendererWhenClean && targetRenderer != null)
            targetRenderer.enabled = false;

        if (targetCollider != null)
            targetCollider.enabled = false;

        if (destroyWhenClean && gameObject.activeInHierarchy && !isFadingOut)
            StartCoroutine(FadeOutAndDestroy());
    }

    void MarkCleaned()
    {
        if (IsCleaned)
            return;

        IsCleaned = true;
        currentDirt = 0f;
        currentCleanPercentage = 1f;
        OnCleaned?.Invoke(this);
    }

    void UpdateVisualState()
    {
        if (isFadingOut) return;

        float dirtPercent = GetDirtPercent();

        if (shrinkWhileCleaning)
        {
            float scaleMultiplier = Mathf.Lerp(minimumScaleMultiplier, 1f, dirtPercent);
            transform.localScale = new Vector3(
                initialLocalScale.x * scaleMultiplier,
                initialLocalScale.y,
                initialLocalScale.z * scaleMultiplier
            );
        }

        if (useDissolveShader && targetRenderer != null)
        {
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(DissolveAmountId, useLocalizedCleaning ? 0f : 1f - dirtPercent);
            propertyBlock.SetFloat(EdgeGlowId, dissolveEdgeGlow);
            propertyBlock.SetFloat(CleanPointCountId, cleanPointCount);
            propertyBlock.SetVectorArray(CleanPointsId, cleanPoints);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    bool AddCleanPoint(Vector3 worldPoint, float worldRadius)
    {
        if (!useDissolveShader || !useLocalizedCleaning || targetRenderer == null) return true;

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        float localRadius = WorldRadiusToLocalRadius(worldRadius);

        CheckPhysicalCleanArea(localPoint, localRadius);

        int existingIndex = FindNearestCleanPoint(localPoint, cleanPointMergeDistance);
        if (existingIndex >= 0)
        {
            if (localRadius > cleanPoints[existingIndex].w + 0.02f)
            {
                cleanPoints[existingIndex].w = localRadius;
                return true;
            }
            return false;
        }

        cleanPoints[nextCleanPointIndex] = new Vector4(localPoint.x, localPoint.y, localPoint.z, localRadius);
        nextCleanPointIndex = (nextCleanPointIndex + 1) % MaxCleanPoints;
        cleanPointCount = Mathf.Min(cleanPointCount + 1, MaxCleanPoints);
        return true;
    }

    bool RestoreAtWorldPoint(Vector3 worldPoint, float worldRadius, float amount)
    {
        if (currentDirt >= maxDirt && currentCleanPercentage <= 0.001f && cleanPointCount == 0)
            return false;

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        float localRadius = WorldRadiusToLocalRadius(worldRadius);
        bool changed = false;

        if (RestorePhysicalArea(localPoint, localRadius))
            changed = true;

        if (RestoreCleanPoints(localPoint, localRadius))
            changed = true;

        float restoredDirt = Mathf.Clamp(currentDirt + amount, 0f, maxDirt);
        if (restoredDirt > currentDirt + 0.001f)
        {
            currentDirt = restoredDirt;
            changed = true;
        }

        if (usePhysicalAreaCheck && totalNodes > 0)
            currentDirt = Mathf.Max(currentDirt, maxDirt * (1f - currentCleanPercentage));

        return changed;
    }

    bool ApplyContaminatedGrowth(float waterAmount)
    {
        contaminatedWaterStored += waterAmount;

        float growthDelta = contaminatedGrowthPerWaterChunk * (waterAmount / contaminatedWaterPerGrowthChunk);
        if (growthDelta <= 0.0001f)
            return false;

        float nextSize = transform.localScale.x + growthDelta;
        ApplySurfaceScale(nextSize);
        ResetDirtyState();
        return true;
    }

    bool RestorePhysicalArea(Vector3 localPoint, float localRadius)
    {
        if (!usePhysicalAreaCheck || totalNodes == 0 || nodeIsClean == null) return false;

        float sqrRadius = localRadius * localRadius;
        bool areaUpdated = false;

        for (int i = 0; i < totalNodes; i++)
        {
            if (!nodeIsClean[i]) continue;
            if ((dirtNodes[i] - localPoint).sqrMagnitude > sqrRadius) continue;

            nodeIsClean[i] = false;
            cleanedNodes = Mathf.Max(0, cleanedNodes - 1);
            areaUpdated = true;
        }

        if (!areaUpdated) return false;

        currentCleanPercentage = totalNodes > 0 ? (float)cleanedNodes / totalNodes : 0f;
        return true;
    }

    bool RestoreCleanPoints(Vector3 localPoint, float localRadius)
    {
        if (!useDissolveShader || !useLocalizedCleaning || cleanPointCount <= 0) return false;

        bool changed = false;
        float mergeRadius = localRadius + cleanPointMergeDistance;
        float sqrMergeRadius = mergeRadius * mergeRadius;

        for (int i = cleanPointCount - 1; i >= 0; i--)
        {
            Vector3 point = new Vector3(cleanPoints[i].x, cleanPoints[i].y, cleanPoints[i].z);
            float pointRadius = cleanPoints[i].w;
            float maxDistance = mergeRadius + pointRadius;

            if ((point - localPoint).sqrMagnitude > Mathf.Max(sqrMergeRadius, maxDistance * maxDistance))
                continue;

            cleanPointCount--;
            cleanPoints[i] = cleanPoints[cleanPointCount];
            cleanPoints[cleanPointCount] = Vector4.zero;
            changed = true;
        }

        if (cleanPointCount <= 0)
        {
            cleanPointCount = 0;
            nextCleanPointIndex = 0;
        }
        else
        {
            nextCleanPointIndex = cleanPointCount % MaxCleanPoints;
        }

        return changed;
    }

    void CheckPhysicalCleanArea(Vector3 localPoint, float localRadius)
    {
        if (!usePhysicalAreaCheck || totalNodes == 0 || isFadingOut) return;

        float sqrRadius = localRadius * localRadius;
        bool areaUpdated = false;

        for (int i = 0; i < totalNodes; i++)
        {
            if (!nodeIsClean[i])
            {
                if ((dirtNodes[i] - localPoint).sqrMagnitude <= sqrRadius)
                {
                    nodeIsClean[i] = true;
                    cleanedNodes++;
                    areaUpdated = true;
                }
            }
        }

        if (areaUpdated)
        {
            currentCleanPercentage = (float)cleanedNodes / totalNodes;
            currentDirt = Mathf.Min(currentDirt, maxDirt * (1f - currentCleanPercentage));

            if (currentCleanPercentage >= cleanCompletionThreshold)
            {
                MarkCleaned();
                UpdateVisualState();
                StartCoroutine(FadeOutAndDestroy());
            }
        }
    }

    int FindNearestCleanPoint(Vector3 localPoint, float localMergeDistance)
    {
        float sqrMergeDistance = localMergeDistance * localMergeDistance;
        for (int i = 0; i < cleanPointCount; i++)
        {
            Vector3 point = new Vector3(cleanPoints[i].x, cleanPoints[i].y, cleanPoints[i].z);
            if ((point - localPoint).sqrMagnitude <= sqrMergeDistance) return i;
        }
        return -1;
    }

    float WorldRadiusToLocalRadius(float worldRadius)
    {
        float surfaceScale = GetSurfaceRadiusScale();
        float localRadius = keepCleaningRadiusWorldSized && surfaceScale > 0.0001f ? worldRadius / surfaceScale : worldRadius;
        return Mathf.Min(localRadius, maxLocalCleanRadius);
    }

    float GetSurfaceRadiusScale()
    {
        Vector3 scale = transform.lossyScale;
        float x = Mathf.Abs(scale.x);
        float y = Mathf.Abs(scale.y);
        float z = Mathf.Abs(scale.z);

        if (targetCollider is MeshCollider)
            return Mathf.Max(x, y);

        return Mathf.Max(x, z);
    }

    void ApplySurfaceScale(float uniformSurfaceScale)
    {
        Vector3 scale = transform.localScale;
        float clampedScale = Mathf.Max(0.01f, uniformSurfaceScale);

        if (targetCollider is MeshCollider)
            transform.localScale = new Vector3(clampedScale, clampedScale, scale.z);
        else
            transform.localScale = new Vector3(clampedScale, scale.y, clampedScale);

        initialLocalScale = transform.localScale;
    }

    void ResetDirtyState()
    {
        isFadingOut = false;
        IsCleaned = false;
        currentDirt = maxDirt;
        currentCleanPercentage = 0f;
        cleanPointCount = 0;
        nextCleanPointIndex = 0;
        cleanedNodes = 0;

        for (int i = 0; i < MaxCleanPoints; i++)
            cleanPoints[i] = Vector4.zero;

        GenerateDirtNodes();

        if (targetCollider != null)
            targetCollider.enabled = true;

        if (targetRenderer != null)
            targetRenderer.enabled = true;
    }

    private IEnumerator FadeOutAndDestroy()
    {
        isFadingOut = true;
        MarkCleaned();
        if (targetCollider != null) targetCollider.enabled = false;

        float fadeDuration = 0.5f;
        float elapsed = 0f;

        if (targetRenderer != null)
        {
            targetRenderer.GetPropertyBlock(propertyBlock);
            float startDissolve = propertyBlock.GetFloat(DissolveAmountId);

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fadeDuration;

                propertyBlock.SetFloat(DissolveAmountId, Mathf.Lerp(startDissolve, 1f, t));
                targetRenderer.SetPropertyBlock(propertyBlock);
                yield return null;
            }
        }

        if (hideRendererWhenClean && targetRenderer != null) targetRenderer.enabled = false;
        if (!destroyWhenClean)
            yield break;

        if (IsSpawned && IsNetworkSessionRunning())
        {
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);

            yield break;
        }

        Destroy(gameObject);
    }
}
