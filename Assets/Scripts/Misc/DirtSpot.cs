using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DirtSpot : MonoBehaviour
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

    private Vector3 initialLocalScale;
    private MaterialPropertyBlock propertyBlock;
    private readonly Vector4[] cleanPoints = new Vector4[MaxCleanPoints];
    private int cleanPointCount;
    private int nextCleanPointIndex;

    private Vector3 lastHitPoint;
    private float lastHitTime = -1f;
    private bool isFadingOut = false;

    private Vector3[] dirtNodes;
    private bool[] nodeIsClean;
    private int totalNodes;
    private int cleanedNodes;

    void Awake()
    {
        if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
        if (targetCollider == null) targetCollider = GetComponent<Collider>();

        initialLocalScale = transform.localScale;
        propertyBlock = new MaterialPropertyBlock();
        currentDirt = Mathf.Clamp(currentDirt, 0f, maxDirt);

        GenerateDirtNodes();
        UpdateVisualState();
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

    public void Clean(float amount)
    {
        if (amount <= 0f || currentDirt <= 0f || isFadingOut) return;

        currentDirt -= amount;
        currentDirt = Mathf.Clamp(currentDirt, 0f, maxDirt);

        UpdateVisualState();

        if (currentDirt <= 0f && !usePhysicalAreaCheck)
            StartCoroutine(FadeOutAndDestroy());
    }

    public void CleanAtWorldPoint(Vector3 worldPoint, float worldRadius, float amount)
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

        if (areaCleaned) Clean(amount);
        else UpdateVisualState();
    }

    public float GetDirtPercent()
    {
        return maxDirt > 0f ? currentDirt / maxDirt : 0f;
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
                currentDirt = 0f;
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
        Vector3 scale = transform.lossyScale;
        float dominantScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        return dominantScale > 0.0001f ? worldRadius / dominantScale : worldRadius;
    }

    private IEnumerator FadeOutAndDestroy()
    {
        isFadingOut = true;
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
        if (destroyWhenClean) Destroy(gameObject);
    }
}