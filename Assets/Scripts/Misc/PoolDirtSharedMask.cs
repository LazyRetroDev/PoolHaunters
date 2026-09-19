using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PoolDirtSharedMask : MonoBehaviour
{
    static readonly int CoverageMaskId = Shader.PropertyToID("_CoverageMask");
    static readonly int UseCoverageMaskId = Shader.PropertyToID("_UseCoverageMask");
    static readonly int CoverageWorldSpaceId = Shader.PropertyToID("_CoverageWorldSpace");
    static readonly int CoverageBoundsId = Shader.PropertyToID("_CoverageBounds");
    static readonly int CoverageWorldToLocalId = Shader.PropertyToID("_CoverageWorldToLocal");

    const int MaskResolution = 512;

    readonly List<Renderer> dirtRenderers = new List<Renderer>();
    Texture2D coverageMask;
    Color32[] pixels;
    Vector4 coverageBounds;
    bool initialized;
    bool uploadPending;
    Matrix4x4 lastWorldToLocalMatrix;

    public bool IsReady
    {
        get
        {
            EnsureInitialized();
            return coverageMask != null;
        }
    }

    public void EnsureInitialized()
    {
        if (initialized)
            return;

        initialized = true;
        DirtSpot[] spots = GetComponentsInChildren<DirtSpot>(true);
        Bounds combinedBounds = default;
        bool hasBounds = false;

        for (int i = 0; i < spots.Length; i++)
        {
            DirtSpot spot = spots[i];
            if (spot == null || spot.createdByContaminatedWater)
                continue;

            Renderer dirtRenderer = spot.targetRenderer != null
                ? spot.targetRenderer
                : spot.GetComponentInChildren<Renderer>(true);
            if (dirtRenderer == null)
                continue;

            dirtRenderers.Add(dirtRenderer);
            EncapsulateRendererBounds(dirtRenderer, ref combinedBounds, ref hasBounds);
        }

        if (!hasBounds)
            return;

        float sizeX = Mathf.Max(0.001f, combinedBounds.size.x);
        float sizeZ = Mathf.Max(0.001f, combinedBounds.size.z);
        coverageBounds = new Vector4(combinedBounds.min.x, combinedBounds.min.z, sizeX, sizeZ);

        pixels = new Color32[MaskResolution * MaskResolution];
        coverageMask = new Texture2D(
            MaskResolution,
            MaskResolution,
            TextureFormat.RGBA32,
            false,
            true)
        {
            name = "Pool dirt shared coverage",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        coverageMask.SetPixels32(pixels);
        coverageMask.Apply(false);
        lastWorldToLocalMatrix = transform.worldToLocalMatrix;
    }

    public bool Paint(Vector3 worldPoint, float worldRadius, bool clean, float softness, float noiseScale)
    {
        EnsureInitialized();
        if (coverageMask == null || worldRadius <= 0f)
            return false;

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        float localRadius = GetLocalRadius(worldRadius);
        int minX = PositionToPixel(localPoint.x - localRadius, coverageBounds.x, coverageBounds.z);
        int maxX = PositionToPixel(localPoint.x + localRadius, coverageBounds.x, coverageBounds.z);
        int minY = PositionToPixel(localPoint.z - localRadius, coverageBounds.y, coverageBounds.w);
        int maxY = PositionToPixel(localPoint.z + localRadius, coverageBounds.y, coverageBounds.w);
        byte targetValue = clean ? (byte)255 : (byte)0;
        bool changed = false;

        for (int y = minY; y <= maxY; y++)
        {
            float localZ = PixelToPosition(y, coverageBounds.y, coverageBounds.w);
            for (int x = minX; x <= maxX; x++)
            {
                int index = y * MaskResolution + x;
                if (pixels[index].r == targetValue)
                    continue;

                float localX = PixelToPosition(x, coverageBounds.x, coverageBounds.z);
                float distance = Vector2.Distance(
                    new Vector2(localX, localZ),
                    new Vector2(localPoint.x, localPoint.z));
                if (distance > localRadius)
                    continue;

                if (clean && softness > 0f)
                {
                    float brush = 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(localRadius * (1f - softness), localRadius, distance));
                    if (brush < CoverageNoise(localX * noiseScale, localZ * noiseScale))
                        continue;
                }

                pixels[index] = new Color32(targetValue, targetValue, targetValue, 255);
                changed = true;
            }
        }

        uploadPending |= changed;
        return changed;
    }

    public void ApplyToPropertyBlock(MaterialPropertyBlock propertyBlock)
    {
        EnsureInitialized();
        if (coverageMask == null || propertyBlock == null)
            return;

        propertyBlock.SetTexture(CoverageMaskId, coverageMask);
        propertyBlock.SetFloat(UseCoverageMaskId, 1f);
        propertyBlock.SetFloat(CoverageWorldSpaceId, 1f);
        propertyBlock.SetVector(CoverageBoundsId, coverageBounds);
        propertyBlock.SetMatrix(CoverageWorldToLocalId, transform.worldToLocalMatrix);
    }

    void LateUpdate()
    {
        if (uploadPending && coverageMask != null)
        {
            uploadPending = false;
            coverageMask.SetPixels32(pixels);
            coverageMask.Apply(false);
        }

        Matrix4x4 currentWorldToLocal = transform.worldToLocalMatrix;
        if (currentWorldToLocal == lastWorldToLocalMatrix)
            return;

        lastWorldToLocalMatrix = currentWorldToLocal;
        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        for (int i = 0; i < dirtRenderers.Count; i++)
        {
            Renderer dirtRenderer = dirtRenderers[i];
            if (dirtRenderer == null)
                continue;

            dirtRenderer.GetPropertyBlock(propertyBlock);
            ApplyToPropertyBlock(propertyBlock);
            dirtRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    void OnDestroy()
    {
        if (coverageMask != null)
            Destroy(coverageMask);
    }

    void EncapsulateRendererBounds(Renderer dirtRenderer, ref Bounds combinedBounds, ref bool hasBounds)
    {
        Bounds worldBounds = dirtRenderer.bounds;
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;

        for (int x = 0; x <= 1; x++)
        for (int y = 0; y <= 1; y++)
        for (int z = 0; z <= 1; z++)
        {
            Vector3 worldCorner = new Vector3(
                x == 0 ? min.x : max.x,
                y == 0 ? min.y : max.y,
                z == 0 ? min.z : max.z);
            Vector3 localCorner = transform.InverseTransformPoint(worldCorner);
            if (!hasBounds)
            {
                combinedBounds = new Bounds(localCorner, Vector3.zero);
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(localCorner);
            }
        }
    }

    float GetLocalRadius(float worldRadius)
    {
        Vector3 localX = transform.InverseTransformVector(Vector3.right * worldRadius);
        Vector3 localZ = transform.InverseTransformVector(Vector3.forward * worldRadius);
        return Mathf.Max(
            new Vector2(localX.x, localX.z).magnitude,
            new Vector2(localZ.x, localZ.z).magnitude);
    }

    static int PositionToPixel(float value, float minimum, float size)
    {
        float normalized = (value - minimum) / Mathf.Max(0.001f, size);
        return Mathf.Clamp(Mathf.FloorToInt(normalized * MaskResolution), 0, MaskResolution - 1);
    }

    static float PixelToPosition(int pixel, float minimum, float size)
    {
        return minimum + (pixel + 0.5f) / MaskResolution * size;
    }

    static float CoverageNoise(float x, float y)
    {
        int ix = Mathf.FloorToInt(x);
        int iy = Mathf.FloorToInt(y);
        float tx = Mathf.SmoothStep(0f, 1f, x - ix);
        float ty = Mathf.SmoothStep(0f, 1f, y - iy);
        return Mathf.Lerp(
            Mathf.Lerp(CoverageHash(ix, iy), CoverageHash(ix + 1, iy), tx),
            Mathf.Lerp(CoverageHash(ix, iy + 1), CoverageHash(ix + 1, iy + 1), tx),
            ty);
    }

    static float CoverageHash(int x, int y)
    {
        unchecked
        {
            uint value = (uint)x * 374761393u + (uint)y * 668265263u;
            value = (value ^ (value >> 13)) * 1274126177u;
            return ((value ^ (value >> 16)) & 65535u) / 65535f;
        }
    }
}
