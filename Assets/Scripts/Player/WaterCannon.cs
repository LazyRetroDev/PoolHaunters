using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;
using Unity.Netcode;

public class WaterCannon : MonoBehaviour
{
    [Header("Follow (Optional)")]
    public Transform followTarget;
    public Vector3 positionOffset;
    public Vector3 rotationOffset;

    [Header("Spray")]
    public Transform sprayOrigin;
    public ParticleSystem sprayParticles;
    public Vector3 defaultSprayLocalOffset = new Vector3(0f, 0f, 0.6f);
    public float waterUsagePerSecond = 10f;
    public float sprayParticleRate = 80f;
    public bool autoCreateSprayParticles = true;

    [Header("Network Visuals")]
    public bool syncSprayVisuals = true;
    public float sprayVisualSyncInterval = 0.05f;
    public float sprayVisualPositionThreshold = 0.03f;
    public float sprayVisualAngleThreshold = 2f;

    [Header("Water Quality Visuals")]
    public Color cleanWaterColor = new Color(0.65f, 0.85f, 1f, 0.8f);
    public Color contaminatedWaterColor = new Color(0.35f, 0.9f, 0.25f, 0.85f);
    public Color chemicallyEnhancedWaterColor = new Color(1f, 0.85f, 0.25f, 0.9f);

    [Header("Cleaning")]
    public float cleanPowerPerSecond = 35f;
    public float sprayDistance = 4f;
    public float sprayRadius = 0.2f;
    public float cleanContactRadius = 0.22f;
    public LayerMask cleanMask = ~0;
    public bool debugSprayRay = false;

    [Header("Contamination")]
    public GameObject contaminatedDirtPrefab;
    public float contaminatedDirtInitialSize = 0.1f;
    public float contaminatedDirtGrowthPerWaterChunk = 1f;
    public float contaminatedDirtWaterPerGrowthChunk = 50f;
    public float contaminatedDirtSurfaceOffset = 0.01f;
    public float contaminatedDirtSearchRadius = 0.35f;

    [Header("Aiming")]
    public Camera aimCamera;
    public LayerMask aimMask = ~0;
    public float aimMaxDistance = 100f;
    public float aimPointDistance = 25f;
    public float aimRotationSharpness = 20f;
    public bool aimAtWorldHitPoint = false;
    public bool debugAimRay = false;
    public bool useScreenCenterWhenCursorLocked = true;

    private PlayerInput playerInput;
    private PlayerMovement playerMovement;
    private PlayerStatus playerStatus;
    private PlayerPetrify playerPetrify;
    private InputAction attackAction;
    private readonly HashSet<DirtSpot> dirtHits = new HashSet<DirtSpot>();
    private readonly HashSet<PoolCleaningZone> poolHits = new HashSet<PoolCleaningZone>();
    private readonly HashSet<GoldenMouthBehavior> goldenMouthHits = new HashSet<GoldenMouthBehavior>();
    private readonly HashSet<TubaraoBehavior> tubaraoHits = new HashSet<TubaraoBehavior>();
    private Transform ownerRoot;
    private WaterQuality appliedVisualQuality;
    private bool hasAppliedVisualQuality;
    private float waterUsageMultiplier = 1f;
    private float waterUsageMultiplierTimer;
    private DirtSpot dirtTemplate;
    private bool publishedSprayPlaying;
    private WaterQuality publishedSprayQuality;
    private Vector3 lastPublishedSprayPosition;
    private Quaternion lastPublishedSprayRotation = Quaternion.identity;
    private bool hasPublishedSprayPose;
    private float nextSprayVisualSyncTime;

    void Awake()
    {
        playerInput = GetComponentInParent<PlayerInput>();
        playerMovement = GetComponentInParent<PlayerMovement>();
        playerStatus = GetComponentInParent<PlayerStatus>();
        playerPetrify = GetComponentInParent<PlayerPetrify>();
        ownerRoot = playerStatus != null ? playerStatus.transform : transform.root;

        if (sprayOrigin == null)
            sprayOrigin = CreateSprayOrigin();

        if (sprayParticles == null && autoCreateSprayParticles)
            sprayParticles = CreateDefaultSprayParticles();

        ApplyParticleSettings();
        UpdateSprayColor(force: true);
        StopSprayImmediate();
    }

    void Start()
    {
        attackAction = playerInput != null ? playerInput.actions["Attack"] : null;

        if (aimCamera == null)
            aimCamera = Camera.main;
    }

    void Update()
    {
        UpdateTimedWaterUsageMultiplier();
        UpdateSprayColor();

        if (!CanOwnerUseWaterCannon())
        {
            StopSpray();
            return;
        }

        if (playerStatus == null || attackAction == null)
        {
            StopSpray();
            return;
        }

        if (!attackAction.IsPressed())
        {
            StopSpray();
            return;
        }

        WaterQuality sprayedWaterQuality = playerStatus.GetWaterQuality();
        float waterThisFrame = waterUsagePerSecond * waterUsageMultiplier * Time.deltaTime;
        if (!playerStatus.ConsumeWater(waterThisFrame))
        {
            StopSpray();
            return;
        }

        float qualityMultiplier = GetCleaningMultiplierForQuality(sprayedWaterQuality);
        StartSpray(sprayedWaterQuality);
        ApplySprayEffects(sprayedWaterQuality, cleanPowerPerSecond * qualityMultiplier * Time.deltaTime, waterThisFrame);
    }

    void LateUpdate()
    {
        if (followTarget != null)
            transform.position = followTarget.TransformPoint(positionOffset);

        AimTowardMouse();

        if (sprayParticles != null && sprayParticles.isPlaying && playerStatus != null)
            PublishSprayVisualIfNeeded(true, playerStatus.GetWaterQuality());
    }

    void OnDisable()
    {
        StopSpray();
        StopSprayImmediate();
    }

    bool CanOwnerUseWaterCannon()
    {
        if (playerStatus != null && !playerStatus.CanAct()) return false;
        return playerPetrify == null || !playerPetrify.IsPetrified();
    }

    public void ApplyWaterUsageMultiplier(float multiplier, float duration)
    {
        waterUsageMultiplier = Mathf.Clamp(multiplier, 0f, 10f);
        waterUsageMultiplierTimer = Mathf.Max(0f, duration);
    }

    void UpdateTimedWaterUsageMultiplier()
    {
        if (waterUsageMultiplierTimer <= 0f) return;

        waterUsageMultiplierTimer -= Time.deltaTime;
        if (waterUsageMultiplierTimer <= 0f)
        {
            waterUsageMultiplierTimer = 0f;
            waterUsageMultiplier = 1f;
        }
    }

    float GetCleaningMultiplierForQuality(WaterQuality waterQuality)
    {
        if (playerStatus == null) return 1f;

        switch (waterQuality)
        {
            case WaterQuality.Contaminated:
                return playerStatus.contaminatedCleaningMultiplier;
            case WaterQuality.ChemicallyEnhanced:
                return playerStatus.chemicallyEnhancedCleaningMultiplier;
            default:
                return 1f;
        }
    }

    Transform CreateSprayOrigin()
    {
        GameObject originObject = new GameObject("WaterSprayOrigin");
        originObject.transform.SetParent(transform, false);
        originObject.transform.localPosition = defaultSprayLocalOffset;
        originObject.transform.localRotation = Quaternion.identity;
        return originObject.transform;
    }

    ParticleSystem CreateDefaultSprayParticles()
    {
        GameObject particlesObject = new GameObject("WaterSprayParticles");
        particlesObject.transform.SetParent(sprayOrigin, false);
        particlesObject.transform.localPosition = Vector3.zero;
        particlesObject.transform.localRotation = Quaternion.identity;

        ParticleSystem particles = particlesObject.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        main.startColor = cleanWaterColor;
        main.gravityModifier = 0.15f;
        main.maxParticles = 250;

        var emission = particles.emission;
        emission.rateOverTime = sprayParticleRate;

        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 8f;
        shape.radius = 0.02f;
        shape.length = 0.1f;

        return particles;
    }

    void ApplyParticleSettings()
    {
        if (sprayParticles == null) return;

        var main = sprayParticles.main;
        main.loop = true;
        main.playOnAwake = false;

        var emission = sprayParticles.emission;
        emission.rateOverTime = sprayParticleRate;
    }

    void UpdateSprayColor(bool force = false)
    {
        if (sprayParticles == null) return;

        WaterQuality quality = playerStatus != null ? playerStatus.GetWaterQuality() : WaterQuality.Clean;
        ApplySprayColor(quality, force);
    }

    void ApplySprayColor(WaterQuality quality, bool force = false)
    {
        if (sprayParticles == null) return;

        if (!force && hasAppliedVisualQuality && appliedVisualQuality == quality) return;

        var main = sprayParticles.main;
        main.startColor = GetWaterColor(quality);
        appliedVisualQuality = quality;
        hasAppliedVisualQuality = true;
    }

    Color GetWaterColor(WaterQuality quality)
    {
        switch (quality)
        {
            case WaterQuality.Contaminated:
                return contaminatedWaterColor;
            case WaterQuality.ChemicallyEnhanced:
                return chemicallyEnhancedWaterColor;
            default:
                return cleanWaterColor;
        }
    }

    void StartSpray(WaterQuality quality)
    {
        ApplySprayColor(quality);

        if (sprayParticles != null && !sprayParticles.isPlaying)
            sprayParticles.Play();

        PublishSprayVisualIfNeeded(true, quality);
    }

    void StopSpray()
    {
        if (sprayParticles != null && sprayParticles.isPlaying)
            sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        WaterQuality quality = playerStatus != null ? playerStatus.GetWaterQuality() : appliedVisualQuality;
        PublishSprayVisualIfNeeded(false, quality);
    }

    void StopSprayImmediate()
    {
        if (sprayParticles != null)
            sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    public void ApplyRemoteSprayVisual(
        bool isSpraying,
        WaterQuality quality,
        Vector3 originPosition,
        Quaternion originRotation)
    {
        if (playerMovement != null &&
            playerMovement.IsSpawned &&
            playerMovement.IsOwner)
        {
            return;
        }

        if (sprayOrigin != null)
            sprayOrigin.SetPositionAndRotation(originPosition, originRotation);

        ApplySprayColor(quality, force: true);

        if (isSpraying)
        {
            if (sprayParticles != null && !sprayParticles.isPlaying)
                sprayParticles.Play();
        }
        else
        {
            StopSprayImmediate();
        }
    }

    void PublishSprayVisualIfNeeded(
        bool isSpraying,
        WaterQuality quality,
        bool force = false)
    {
        if (!ShouldPublishSprayVisual())
            return;

        Vector3 originPosition = sprayOrigin != null
            ? sprayOrigin.position
            : transform.position;
        Quaternion originRotation = sprayOrigin != null
            ? sprayOrigin.rotation
            : transform.rotation;

        if (!force && !ShouldSendSprayVisual(
            isSpraying,
            quality,
            originPosition,
            originRotation))
        {
            return;
        }

        publishedSprayPlaying = isSpraying;
        publishedSprayQuality = quality;
        lastPublishedSprayPosition = originPosition;
        lastPublishedSprayRotation = originRotation;
        hasPublishedSprayPose = true;
        nextSprayVisualSyncTime = Time.time + Mathf.Max(0.01f, sprayVisualSyncInterval);

        playerMovement.PublishWaterSprayVisual(
            isSpraying,
            quality,
            originPosition,
            originRotation);
    }

    bool ShouldPublishSprayVisual()
    {
        return syncSprayVisuals &&
            playerMovement != null &&
            playerMovement.IsSpawned &&
            playerMovement.IsOwner;
    }

    bool ShouldSendSprayVisual(
        bool isSpraying,
        WaterQuality quality,
        Vector3 originPosition,
        Quaternion originRotation)
    {
        if (publishedSprayPlaying != isSpraying)
            return true;

        if (!isSpraying)
            return false;

        if (publishedSprayQuality != quality)
            return true;

        if (!hasPublishedSprayPose || Time.time >= nextSprayVisualSyncTime)
            return true;

        float positionThreshold = Mathf.Max(0f, sprayVisualPositionThreshold);
        if ((originPosition - lastPublishedSprayPosition).sqrMagnitude >
            positionThreshold * positionThreshold)
        {
            return true;
        }

        return Quaternion.Angle(originRotation, lastPublishedSprayRotation) >
            sprayVisualAngleThreshold;
    }

    void AimTowardMouse()
    {
        if (aimCamera == null)
            aimCamera = Camera.main;

        if (aimCamera == null || sprayOrigin == null) return;

        Vector2 pointerPosition = GetPointerScreenPosition();

        Ray aimRay = aimCamera.ScreenPointToRay(pointerPosition);
        Vector3 aimPoint = GetAimPoint(aimRay);
        Vector3 direction = aimPoint - sprayOrigin.position;

        if (debugAimRay)
            Debug.DrawRay(aimRay.origin, aimRay.direction * aimMaxDistance, Color.yellow);

        if (direction.sqrMagnitude <= 0.001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized) * Quaternion.Euler(rotationOffset);
        float blend = 1f - Mathf.Exp(-aimRotationSharpness * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, blend);
    }

    void ApplySprayEffects(WaterQuality waterQuality, float cleanAmount, float waterAmount)
    {
        if (sprayOrigin == null || waterAmount <= 0f) return;

        dirtHits.Clear();
        poolHits.Clear();
        goldenMouthHits.Clear();
        tubaraoHits.Clear();

        bool handledContaminatedDirt = false;
        RaycastHit? contaminationSurfaceHit = null;

        Ray ray = new Ray(sprayOrigin.position, sprayOrigin.forward);
        RaycastHit[] hits = Physics.SphereCastAll(ray, sprayRadius, sprayDistance, cleanMask, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        if (debugSprayRay)
            Debug.DrawRay(ray.origin, ray.direction * sprayDistance, Color.cyan);

        for (int i = 0; i < hits.Length; i++)
        {
            if (ShouldIgnoreHit(hits[i]))
                continue;

            DirtSpot dirtSpot = hits[i].collider.GetComponentInParent<DirtSpot>();
            PoolCleaningZone pool = hits[i].collider.GetComponentInParent<PoolCleaningZone>();

            if (pool != null && !poolHits.Contains(pool))
            {
                poolHits.Add(pool);
                pool.ApplyWaterAtWorldPoint(
                    hits[i].point,
                    cleanContactRadius,
                    cleanAmount,
                    waterAmount,
                    waterQuality);

                if (waterQuality == WaterQuality.Contaminated)
                    handledContaminatedDirt = true;
            }

            if (waterQuality == WaterQuality.Contaminated)
            {
                if (dirtSpot != null)
                {
                    if (!dirtHits.Contains(dirtSpot))
                    {
                        dirtHits.Add(dirtSpot);
                        dirtSpot.ApplyContaminatedWaterAtWorldPoint(hits[i].point, cleanContactRadius, waterAmount);
                    }

                    handledContaminatedDirt = true;
                }
                else if (!contaminationSurfaceHit.HasValue && IsValidContaminationSurface(hits[i]))
                {
                    contaminationSurfaceHit = hits[i];
                }
            }
            else if (dirtSpot != null && !dirtHits.Contains(dirtSpot))
            {
                dirtHits.Add(dirtSpot);
                dirtSpot.CleanAtWorldPoint(
                    hits[i].point,
                    cleanContactRadius,
                    cleanAmount,
                    playerStatus);
            }

            GoldenMouthBehavior goldenMouth = hits[i].collider.GetComponentInParent<GoldenMouthBehavior>();
            if (goldenMouth != null && !goldenMouthHits.Contains(goldenMouth))
            {
                goldenMouthHits.Add(goldenMouth);
                ApplyWaterToGoldenMouth(goldenMouth, waterQuality, cleanAmount);
            }

            TubaraoBehavior tubarao = hits[i].collider.GetComponentInParent<TubaraoBehavior>();
            if (tubarao != null && !tubaraoHits.Contains(tubarao))
            {
                tubaraoHits.Add(tubarao);
                ApplyWaterToTubarao(tubarao, sprayOrigin.position);
            }
        }

        if (waterQuality == WaterQuality.Contaminated && !handledContaminatedDirt && contaminationSurfaceHit.HasValue)
            CreateOrGrowContaminatedDirt(contaminationSurfaceHit.Value, waterAmount);
    }

    void ApplyWaterToTubarao(TubaraoBehavior tubarao, Vector3 sourcePosition)
    {
        if (tubarao == null)
            return;

        if (playerMovement != null)
        {
            playerMovement.RequestEnemyWaterHit(tubarao.gameObject, sourcePosition);
            return;
        }

        tubarao.ReceiveWaterHit(sourcePosition);
    }

    void ApplyWaterToGoldenMouth(
        GoldenMouthBehavior goldenMouth,
        WaterQuality waterQuality,
        float cleanAmount)
    {
        if (goldenMouth == null)
            return;

        if (playerMovement != null)
        {
            playerMovement.RequestApplyWaterToGoldenMouth(
                goldenMouth,
                waterQuality,
                cleanAmount);
            return;
        }

        goldenMouth.ApplyWater(waterQuality, cleanAmount);
    }

    bool ShouldIgnoreHit(RaycastHit hit)
    {
        if (hit.collider == null) return true;
        if (ownerRoot != null && hit.collider.transform.IsChildOf(ownerRoot)) return true;
        return false;
    }

    bool IsValidContaminationSurface(RaycastHit hit)
    {
        if (hit.collider == null || hit.collider.isTrigger) return false;
        if (ownerRoot != null && hit.collider.transform.IsChildOf(ownerRoot)) return false;
        if (hit.collider.GetComponentInParent<PlayerStatus>() != null) return false;
        return true;
    }

    void CreateOrGrowContaminatedDirt(RaycastHit hit, float waterAmount)
    {
        Vector3 contactPoint = hit.point + hit.normal * contaminatedDirtSurfaceOffset;
        if (ShouldRequestServerContaminatedDirt())
        {
            playerMovement.RequestCreateOrGrowContaminatedDirt(
                contactPoint,
                hit.normal,
                cleanContactRadius,
                waterAmount);
            return;
        }

        CreateOrGrowContaminatedDirtAtPoint(
            contactPoint,
            hit.normal,
            cleanContactRadius,
            waterAmount);
    }

    public void CreateOrGrowContaminatedDirtFromNetwork(
        Vector3 contactPoint,
        Vector3 surfaceNormal,
        float contactRadius,
        float waterAmount)
    {
        if (!IsFiniteVector3(contactPoint) ||
            !IsFiniteVector3(surfaceNormal) ||
            !HasValidAmount(contactRadius) ||
            !HasValidAmount(waterAmount))
        {
            return;
        }

        CreateOrGrowContaminatedDirtAtPoint(
            contactPoint,
            surfaceNormal,
            contactRadius,
            waterAmount);
    }

    void CreateOrGrowContaminatedDirtAtPoint(
        Vector3 contactPoint,
        Vector3 surfaceNormal,
        float contactRadius,
        float waterAmount)
    {
        DirtSpot dirtSpot = FindNearbyDirtSpot(contactPoint, contaminatedDirtSearchRadius);
        if (dirtSpot == null)
            dirtSpot = SpawnContaminatedDirt(contactPoint, surfaceNormal);

        if (dirtSpot == null) return;

        dirtSpot.ApplyContaminatedWaterAtWorldPoint(contactPoint, contactRadius, waterAmount);
    }

    bool ShouldRequestServerContaminatedDirt()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return playerMovement != null &&
            playerMovement.IsSpawned &&
            !playerMovement.IsServer &&
            networkManager != null &&
            networkManager.IsListening;
    }

    DirtSpot FindNearbyDirtSpot(Vector3 worldPoint, float searchRadius)
    {
        Collider[] nearbyColliders = Physics.OverlapSphere(worldPoint, searchRadius, ~0, QueryTriggerInteraction.Collide);
        DirtSpot nearestDirtSpot = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < nearbyColliders.Length; i++)
        {
            DirtSpot dirtSpot = nearbyColliders[i].GetComponentInParent<DirtSpot>();
            if (dirtSpot == null) continue;

            float distance = Vector3.Distance(worldPoint, dirtSpot.transform.position);
            if (distance >= nearestDistance) continue;

            nearestDistance = distance;
            nearestDirtSpot = dirtSpot;
        }

        return nearestDirtSpot;
    }

    DirtSpot SpawnContaminatedDirt(Vector3 position, Vector3 surfaceNormal)
    {
        Quaternion rotation = Quaternion.LookRotation(-surfaceNormal);
        GameObject dirtObject = null;

        if (contaminatedDirtPrefab != null)
        {
            dirtObject = Instantiate(contaminatedDirtPrefab, position, rotation);
        }
        else if (TryResolveDirtTemplate(out DirtSpot template))
        {
            dirtObject = Instantiate(template.gameObject, position, rotation);
        }

        if (dirtObject == null) return null;

        dirtObject.name = "ContaminatedDirtSpot";

        DirtSpot dirtSpot = dirtObject.GetComponent<DirtSpot>();
        if (dirtSpot == null) return null;

        if (!TrySpawnNetworkDirtObject(dirtObject))
            return null;

        dirtSpot.ConfigureGeneratedContaminatedSpot(
            contaminatedDirtInitialSize,
            contaminatedDirtGrowthPerWaterChunk,
            contaminatedDirtWaterPerGrowthChunk);

        return dirtSpot;
    }

    bool TrySpawnNetworkDirtObject(GameObject dirtObject)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return true;

        if (!networkManager.IsServer)
        {
            Destroy(dirtObject);
            return false;
        }

        NetworkObject networkObject = dirtObject.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogWarning(
                $"Contaminated dirt prefab '{dirtObject.name}' needs a NetworkObject for multiplayer spawning.");
            Destroy(dirtObject);
            return false;
        }

        if (!networkObject.IsSpawned)
            networkObject.Spawn(true);

        return true;
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

    bool TryResolveDirtTemplate(out DirtSpot template)
    {
        if (dirtTemplate != null)
        {
            template = dirtTemplate;
            return true;
        }

        dirtTemplate = FindObjectOfType<DirtSpot>();
        template = dirtTemplate;
        return template != null;
    }

    Vector3 GetAimPoint(Ray aimRay)
    {
        if (!aimAtWorldHitPoint)
            return aimRay.GetPoint(aimPointDistance);

        RaycastHit[] hits = Physics.RaycastAll(aimRay, aimMaxDistance, aimMask, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            if (ownerRoot != null && hits[i].collider.transform.IsChildOf(ownerRoot))
                continue;

            return hits[i].point;
        }

        return aimRay.GetPoint(aimPointDistance);
    }

    Vector2 GetPointerScreenPosition()
    {
        if (aimCamera == null)
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        bool rendersToTexture = aimCamera.targetTexture != null;
        Rect cameraRect = aimCamera.pixelRect;
        Vector2 cameraCenter = rendersToTexture
            ? new Vector2(aimCamera.pixelWidth * 0.5f, aimCamera.pixelHeight * 0.5f)
            : cameraRect.center;

        if (useScreenCenterWhenCursorLocked && Cursor.lockState == CursorLockMode.Locked)
            return cameraCenter;

        if (Mouse.current == null)
            return cameraCenter;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        if (!rendersToTexture)
            return mousePosition;

        float normalizedX = Screen.width > 0 ? Mathf.Clamp01(mousePosition.x / Screen.width) : 0.5f;
        float normalizedY = Screen.height > 0 ? Mathf.Clamp01(mousePosition.y / Screen.height) : 0.5f;
        return new Vector2(normalizedX * aimCamera.pixelWidth, normalizedY * aimCamera.pixelHeight);
    }
}
