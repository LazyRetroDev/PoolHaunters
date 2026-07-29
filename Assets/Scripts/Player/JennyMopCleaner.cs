using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class JennyMopCleaner : MonoBehaviour
{
    [Header("Input")]
    public string attackActionName = "Attack";
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

    private readonly HashSet<DirtSpot> dirtHits = new HashSet<DirtSpot>();
    private readonly HashSet<PoolCleaningZone> poolHits = new HashSet<PoolCleaningZone>();
    private PlayerInput playerInput;
    private PlayerMovement movement;
    private PlayerStatus playerStatus;
    private PlayerPetrify petrify;
    private InputAction attackAction;
    private InputAction moveAction;
    private Renderer placeholderRenderer;

    void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        movement = GetComponent<PlayerMovement>();
        playerStatus = GetComponent<PlayerStatus>();
        petrify = GetComponent<PlayerPetrify>();

        if (playerInput != null)
        {
            attackAction = playerInput.actions.FindAction(attackActionName, false);
            moveAction = playerInput.actions.FindAction("Move", false);
        }

        if (mopHead == null)
            mopHead = CreateMopHead();
    }

    void Update()
    {
        UpdateMopPose();

        if (!CanMop())
            return;

        SweepClean();
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
            return false;

        if (!requireMovement || moveAction == null)
            return true;

        return moveAction.ReadValue<Vector2>().sqrMagnitude > 0.05f;
    }

    void OnDisable()
    {
        if (hideMopWhenDisabled && mopHead != null)
            mopHead.gameObject.SetActive(false);
    }

    void SweepClean()
    {
        dirtHits.Clear();
        poolHits.Clear();

        Vector3 center = GetMopWorldPosition();
        Quaternion rotation = GetMopWorldRotation();
        Collider[] hits = Physics.OverlapBox(
            center,
            mopHalfExtents,
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
                dirt.CleanAtWorldPoint(
                    contactPoint,
                    cleanContactRadius,
                    cleanPowerPerSecond * Time.deltaTime);
            }

            PoolCleaningZone pool = hit.GetComponentInParent<PoolCleaningZone>();
            if (pool != null && !poolHits.Contains(pool))
            {
                poolHits.Add(pool);
                pool.Clean(poolCleanPowerPerSecond * Time.deltaTime);
            }
        }
    }

    void UpdateMopPose()
    {
        if (mopHead == null)
            return;

        mopHead.position = GetMopWorldPosition();
        mopHead.rotation = GetMopWorldRotation();
    }

    Vector3 GetMopWorldPosition()
    {
        Vector3 basePosition = transform.TransformPoint(mopLocalOffset);
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

    Quaternion GetMopWorldRotation()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.001f)
            forward = transform.forward;

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    Transform CreateMopHead()
    {
        GameObject mopObject = new GameObject("JennyMopHead");
        mopObject.transform.SetParent(transform, false);

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
        Gizmos.DrawCube(Vector3.zero, mopHalfExtents * 2f);
        Gizmos.matrix = previousMatrix;
    }
}
