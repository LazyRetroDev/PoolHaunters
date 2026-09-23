using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class LouiseFishingCleaner : MonoBehaviour
{
    [Header("Casting")]
    public float chargeSeconds = 1.2f;
    public float minimumRange = 3f;
    public float maximumRange = 15f;
    public float tetherRange = 20f;
    public float castSpeed = 22f;
    public float recallSpeed = 30f;
    public LayerMask surfaceMask = ~0;
    [Header("Cleaning")]
    public float cleaningRadius = 0.8f;
    [Min(0.05f)] public float maximumCleaningRadius = 2f;
    [Min(0f)] public float radiusGrowthPerSecond = 0.2f;
    public float cleaningPowerPerSecond = 35f;
    public float waterPerSecond = 6f;
    [Min(0.25f)] public float tickInterval = 0.25f;
    [Header("Presentation")]
    public Transform rodTip;
    public Transform lureVisual;
    public Camera aimCamera;
    public Vector3 rodLocalPosition = new Vector3(0.2f, 0.15f, 0.45f);
    public Vector3 rodLocalEulerAngles = new Vector3(55f, 0f, 0f);

    enum State { Idle, Charging, Flying, Attached, Returning }
    State state;
    PlayerMovement movement;
    PlayerStatus status;
    PlayerPetrify petrify;
    InputAction attack;
    InputAction recall;
    Transform attachedTo;
    Vector3 localPoint, localNormal, flightDirection;
    float charge, flightRemaining, nextTick;
    float attachedCleaningSeconds;
    float nextVisualMessage;
    public float CurrentCleaningRadius => Mathf.Min(
        Mathf.Max(0.05f, cleaningRadius) + Mathf.Max(0f, radiusGrowthPerSecond) * attachedCleaningSeconds,
        Mathf.Max(Mathf.Max(0.05f, cleaningRadius), maximumCleaningRadius));
    LineRenderer line;
    Transform generatedRod;
    Material visualMaterial;
    Vector3 lureWorldPosition;
    Vector3 attachmentPoint, attachmentNormal;
    readonly HashSet<DirtSpot> dirtHits = new HashSet<DirtSpot>();
    readonly HashSet<PoolWaterReactive> reactiveHits = new HashSet<PoolWaterReactive>();
    readonly HashSet<PoolCleaningZone> poolHits = new HashSet<PoolCleaningZone>();
    readonly RaycastHit[] traceHits = new RaycastHit[32];

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        status = GetComponent<PlayerStatus>();
        petrify = GetComponent<PlayerPetrify>();
        var input = GetComponent<PlayerInput>();
        attack = input != null && input.actions != null ? input.actions.FindAction("Attack", false) : null;
        recall = input != null && input.actions != null ? input.actions.FindAction("RightClick", false) : null;
    }

    bool CanUse()
    {
        return (movement == null || ((!movement.IsSpawned || movement.IsOwner) && movement.AcceptsInput)) &&
            status != null && status.CanAct() && (petrify == null || !petrify.IsPetrified());
    }

    void Update()
    {
        if (!CanUse())
        {
            // Menus suspend tool use, not the held model or its attachment.
            bool keepVisible = status != null && !status.IsDead() && !status.IsKnockedOut() &&
                !status.IsTransformed() && (petrify == null || !petrify.IsPetrified()) &&
                (movement == null || !movement.IsSpawned || movement.IsOwner);
            if (!keepVisible)
            {
                ResetTool();
                if (generatedRod != null) generatedRod.gameObject.SetActive(false);
            }
            return;
        }
        if (aimCamera == null) aimCamera = GetComponentInChildren<Camera>();
        // The scene camera can follow the player without being parented to it.
        if (aimCamera == null) aimCamera = Camera.main;
        if (aimCamera == null) return;
        EnsureVisuals();
        if (rodTip == null || lureVisual == null || line == null) return;
        if (generatedRod != null)
        {
            generatedRod.localPosition = rodLocalPosition;
            generatedRod.localRotation = Quaternion.Euler(rodLocalEulerAngles);
        }
        // Flight is world-space even though the visual is owned by the player hierarchy.
        lureVisual.position = state == State.Idle ? rodTip.position : lureWorldPosition;
        bool pressed = attack != null ? attack.IsPressed() : Mouse.current != null && Mouse.current.leftButton.isPressed;
        bool recallPressed = recall != null ? recall.WasPressedThisFrame() : Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        if (recallPressed && state != State.Idle) state = State.Returning;
        if (state == State.Idle && pressed) { state = State.Charging; charge = 0f; }
        if (state == State.Charging)
        {
            charge = Mathf.Clamp01(charge + Time.deltaTime / Mathf.Max(0.1f, chargeSeconds));
            float previewRange = Mathf.Lerp(minimumRange, maximumRange, charge);
            lureVisual.position = Trace(aimCamera.transform.position, aimCamera.transform.forward, previewRange, out RaycastHit preview)
                ? preview.point + preview.normal * 0.06f
                : aimCamera.transform.position + aimCamera.transform.forward * previewRange;
            if (!pressed)
            {
                Vector3 destination = preview.collider != null ? preview.point : lureVisual.position;
                lureVisual.position = rodTip.position;
                flightDirection = (destination - rodTip.position).normalized;
                flightRemaining = Vector3.Distance(destination, rodTip.position) + 0.1f;
                state = State.Flying;
            }
        }
        else if (state == State.Flying)
        {
            float distance = Mathf.Min(flightRemaining, Mathf.Max(1f, castSpeed) * Time.deltaTime);
            if (Trace(lureVisual.position, flightDirection, distance, out RaycastHit hit))
            {
                attachedTo = hit.transform;
                localPoint = attachedTo.InverseTransformPoint(hit.point);
                localNormal = attachedTo.InverseTransformDirection(hit.normal);
                attachmentPoint = hit.point;
                attachmentNormal = hit.normal;
                state = State.Attached;
                attachedCleaningSeconds = 0f;
                nextTick = Time.time;
            }
            else
            {
                lureVisual.position += flightDirection * distance;
                flightRemaining -= distance;
                if (flightRemaining <= 0f) state = State.Returning;
            }
        }
        else if (state == State.Attached)
        {
            if (attachedTo == null || !attachedTo.gameObject.activeInHierarchy)
            {
                // A cleaned decal can disappear before its neighbours. Keep the lure on the backing surface.
                if (Trace(attachmentPoint + attachmentNormal * 0.12f, -attachmentNormal, 0.5f, out RaycastHit support))
                {
                    attachedTo = support.transform;
                    localPoint = attachedTo.InverseTransformPoint(support.point);
                    localNormal = attachedTo.InverseTransformDirection(support.normal);
                }
                else state = State.Returning;
            }
            if (state == State.Attached)
            {
                Vector3 point = attachedTo.TransformPoint(localPoint);
                Vector3 normal = attachedTo.TransformDirection(localNormal).normalized;
                attachmentPoint = point;
                attachmentNormal = normal;
                lureVisual.position = point + normal * 0.06f;
                if (Time.time >= nextTick)
                {
                    float step = Mathf.Max(0.25f, tickInterval);
                    nextTick = Time.time + step;
                    Clean(point, normal, step);
                }
            }
        }
        if (Vector3.Distance(rodTip.position, lureVisual.position) > Mathf.Max(maximumRange, tetherRange)) state = State.Returning;
        if (state == State.Returning)
        {
            lureVisual.position = Vector3.MoveTowards(lureVisual.position, rodTip.position, Mathf.Max(1f, recallSpeed) * Time.deltaTime);
            if (Vector3.Distance(lureVisual.position, rodTip.position) < 0.1f) ResetTool();
        }
        if (state == State.Idle)
            lureVisual.position = rodTip.position - transform.up * 0.15f;
        lureWorldPosition = lureVisual.position;
        lureVisual.gameObject.SetActive(true);
        line.enabled = true;
        line.SetPosition(0, rodTip.position);
        line.SetPosition(1, lureVisual.position);
    }

    void LateUpdate()
    {
        if (movement != null && movement.IsSpawned && movement.IsOwner && Time.unscaledTime >= nextVisualMessage)
        {
            nextVisualMessage = Time.unscaledTime + 0.1f;
            bool visible = generatedRod != null && generatedRod.gameObject.activeSelf && rodTip != null && lureVisual != null;
            movement.PublishLouiseRod(visible, visible ? generatedRod.position : transform.position,
                visible ? generatedRod.rotation : transform.rotation,
                visible ? rodTip.position : transform.position, visible ? lureVisual.position : transform.position);
        }
        if (line == null || !line.enabled || lureVisual == null || rodTip == null) return;
        lureVisual.position = state == State.Idle
            ? rodTip.position - transform.up * 0.15f : lureWorldPosition;
        line.SetPosition(0, rodTip.position);
        line.SetPosition(1, lureVisual.position);
    }

    bool Trace(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default;
        float closest = float.PositiveInfinity;
        int count = Physics.RaycastNonAlloc(origin, direction, traceHits, distance, surfaceMask, QueryTriggerInteraction.Collide);
        // A saturated non-alloc query may omit the nearest wall. Preserve occlusion in crowded scenes.
        RaycastHit[] hits = count == traceHits.Length
            ? Physics.RaycastAll(origin, direction, distance, surfaceMask, QueryTriggerInteraction.Collide) : traceHits;
        if (hits != traceHits) count = hits.Length;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.transform.IsChildOf(transform) || hit.distance >= closest) continue;
            if (hit.collider.isTrigger && hit.collider.GetComponentInParent<DirtSpot>() == null &&
                hit.collider.GetComponentInParent<PoolWaterReactive>() == null && hit.collider.GetComponentInParent<PoolCleaningZone>() == null) continue;
            closest = hit.distance;
            nearest = hit;
        }
        return closest < float.PositiveInfinity;
    }

    void Clean(Vector3 point, Vector3 normal, float seconds)
    {
        float water = Mathf.Max(0f, waterPerSecond) * seconds;
        WaterQuality quality = status.GetWaterQuality();
        if (!status.ConsumeWater(water)) { state = State.Returning; return; }
        // Count paid cleaning ticks, not wall-clock time spent paused or out of water.
        attachedCleaningSeconds += seconds;
        float power = Mathf.Max(0f, cleaningPowerPerSecond) * seconds * (quality == WaterQuality.ChemicallyEnhanced ? 1.35f : 1f);
        float radius = CurrentCleaningRadius;
        Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        Vector3 bitangent = Vector3.Cross(normal, tangent);
        reactiveHits.Clear();
        poolHits.Clear();
        dirtHits.Clear();
        // Sample only the exposed face, never overlapping blindly through a wall.
        for (int i = 0; i < 9; i++)
        {
            float angle = (i - 1) * Mathf.PI / 4f;
            Vector3 offset = i == 0 ? Vector3.zero : (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * radius * 0.65f;
            if ((i == 0 || !Trace(point + normal * 0.12f, offset.normalized, offset.magnitude, out _)) &&
                Trace(point + offset + normal * 0.12f, -normal, 0.24f, out RaycastHit hit))
            {
                var dirt = hit.collider.GetComponentInParent<DirtSpot>();
                if (dirt != null && dirtHits.Add(dirt))
                {
                    // One continuous brush footprint, not nine small circles with scalloped edges.
                    if (quality == WaterQuality.Contaminated) dirt.ApplyContaminatedWaterAtWorldPoint(point, radius, water);
                    else dirt.CleanAtWorldPoint(point, radius, power, status);
                }
                var reactive = hit.collider.GetComponentInParent<PoolWaterReactive>();
                if (reactive != null && reactiveHits.Add(reactive)) reactive.ApplyPoolWaterHit(quality, power, transform.position);
                var pool = hit.collider.GetComponentInParent<PoolCleaningZone>();
                if (pool != null && poolHits.Add(pool)) pool.ApplyWaterAtWorldPoint(point, radius, power, water, quality, status);
                if (movement != null && i == 0)
                {
                    var goldenMouth = hit.collider.GetComponentInParent<GoldenMouthBehavior>();
                    if (goldenMouth != null) movement.RequestApplyWaterToGoldenMouth(goldenMouth, quality, power);
                    else movement.RequestEnemyWaterHit(hit.collider.gameObject, transform.position);
                }
            }
        }
    }

    void EnsureVisuals()
    {
        if (visualMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return;
            visualMaterial = new Material(shader);
            visualMaterial.color = new Color(0.2f, 0.9f, 0.8f);
        }
        if (rodTip == null)
        {
            generatedRod = MakeVisual("Louise Rod", PrimitiveType.Cube, new Vector3(0.035f, 0.85f, 0.035f));
            rodTip = new GameObject("Rod Tip").transform;
            rodTip.SetParent(generatedRod, false);
            rodTip.localPosition = Vector3.up * 0.5f;
        }
        if (lureVisual == null) lureVisual = MakeVisual("Louise Lure", PrimitiveType.Sphere, Vector3.one * 0.14f);
        if (line == null)
        {
            line = new GameObject("Louise Fishing Line").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.sharedMaterial = visualMaterial;
            line.positionCount = 2;
            line.startWidth = line.endWidth = 0.015f;
            line.useWorldSpace = true;
        }
        if (generatedRod != null) generatedRod.gameObject.SetActive(true);
    }

    Transform MakeVisual(string objectName, PrimitiveType primitive, Vector3 scale)
    {
        GameObject obj = GameObject.CreatePrimitive(primitive);
        obj.name = objectName;
        obj.transform.SetParent(transform, false);
        obj.transform.localScale = scale;
        obj.GetComponent<Collider>().enabled = false;
        Destroy(obj.GetComponent<Collider>());
        obj.GetComponent<Renderer>().sharedMaterial = visualMaterial;
        return obj.transform;
    }

    void ResetTool()
    {
        state = State.Idle;
        attachedCleaningSeconds = 0f;
        attachedTo = null;
        if (lureVisual != null) lureVisual.gameObject.SetActive(false);
        if (line != null) line.enabled = false;
    }
    void OnDisable()
    {
        ResetTool();
        if (generatedRod != null) generatedRod.gameObject.SetActive(false);
        if (movement != null) movement.PublishLouiseRod(false, transform.position, transform.rotation, transform.position, transform.position);
    }
    void OnDestroy() { if (visualMaterial != null) Destroy(visualMaterial); }
}
