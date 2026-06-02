using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;

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

    [Header("Cleaning")]
    public float cleanPowerPerSecond = 35f;
    public float sprayDistance = 4f;
    public float sprayRadius = 0.2f;
    public float cleanContactRadius = 0.22f;
    public LayerMask cleanMask = ~0;
    public bool debugSprayRay = false;

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
    private PlayerStatus playerStatus;
    private InputAction attackAction;
    private readonly HashSet<DirtSpot> dirtHits = new HashSet<DirtSpot>();
    private Transform ownerRoot;

    void Awake()
    {
        playerInput = GetComponentInParent<PlayerInput>();
        playerStatus = GetComponentInParent<PlayerStatus>();
        ownerRoot = playerStatus != null ? playerStatus.transform : transform.root;

        if (sprayOrigin == null)
            sprayOrigin = CreateSprayOrigin();

        if (sprayParticles == null && autoCreateSprayParticles)
            sprayParticles = CreateDefaultSprayParticles();

        ApplyParticleSettings();
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

        float waterThisFrame = waterUsagePerSecond * Time.deltaTime;
        if (!playerStatus.ConsumeWater(waterThisFrame))
        {
            StopSpray();
            return;
        }

        float qualityMultiplier = playerStatus.GetWaterCleaningMultiplier();
        StartSpray();
        CleanDirt(cleanPowerPerSecond * qualityMultiplier * Time.deltaTime);
    }

    void LateUpdate()
    {
        if (followTarget != null)
            transform.position = followTarget.TransformPoint(positionOffset);

        AimTowardMouse();
    }

    void OnDisable()
    {
        StopSprayImmediate();
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
        main.startColor = new Color(0.65f, 0.85f, 1f, 0.8f);
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

    void StartSpray()
    {
        if (sprayParticles != null && !sprayParticles.isPlaying)
            sprayParticles.Play();
    }

    void StopSpray()
    {
        if (sprayParticles != null && sprayParticles.isPlaying)
            sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void StopSprayImmediate()
    {
        if (sprayParticles != null)
            sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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

    void CleanDirt(float cleanAmount)
    {
        if (sprayOrigin == null || cleanAmount <= 0f) return;

        dirtHits.Clear();

        Ray ray = new Ray(sprayOrigin.position, sprayOrigin.forward);
        RaycastHit[] hits = Physics.SphereCastAll(ray, sprayRadius, sprayDistance, cleanMask, QueryTriggerInteraction.Collide);

        if (debugSprayRay)
            Debug.DrawRay(ray.origin, ray.direction * sprayDistance, Color.cyan);

        for (int i = 0; i < hits.Length; i++)
        {
            DirtSpot dirtSpot = hits[i].collider.GetComponentInParent<DirtSpot>();
            if (dirtSpot == null || dirtHits.Contains(dirtSpot)) continue;

            dirtHits.Add(dirtSpot);
            dirtSpot.CleanAtWorldPoint(hits[i].point, cleanContactRadius, cleanAmount);
        }
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
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        if (useScreenCenterWhenCursorLocked && Cursor.lockState == CursorLockMode.Locked)
            return screenCenter;

        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();

        return screenCenter;
    }
}
