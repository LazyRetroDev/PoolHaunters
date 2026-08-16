using System;
using UnityEngine;

[DisallowMultipleComponent]
public class PoolCleaningZone : MonoBehaviour
{
    [Header("Cleaning")]
    public float maxContamination = 100f;
    public float currentContamination = 100f;
    [Range(0.01f, 1f)] public float cleanCompletionThreshold = 0.98f;
    public bool startsContaminated = true;
    public bool forwardWaterToPoolDirtSpots = true;
    [Min(1f)] public float forwardedDirtRadiusMultiplier = 1.4f;

    [Header("Water Effects")]
    public bool contaminatedWaterDirtiesPool = true;
    public float contaminatedWaterPerDirt = 0.5f;

    [Header("Setup")]
    public Collider cleaningCollider;
    public bool forceColliderAsTrigger = true;

    public event Action<PoolCleaningZone> OnCleaned;
    public event Action<PoolCleaningZone> OnProgressChanged;

    public bool IsCleaned { get; private set; }

    public float CleanPercent
    {
        get
        {
            if (maxContamination <= 0f)
                return 1f;

            return Mathf.Clamp01(1f - currentContamination / maxContamination);
        }
    }

    void Awake()
    {
        ResolveCollider();

        maxContamination = Mathf.Max(1f, maxContamination);
        currentContamination = startsContaminated
            ? Mathf.Clamp(currentContamination, 0f, maxContamination)
            : 0f;

        RefreshCleanState(false);
    }

    void Reset()
    {
        ResolveCollider();
    }

    void OnValidate()
    {
        maxContamination = Mathf.Max(1f, maxContamination);
        currentContamination = Mathf.Clamp(currentContamination, 0f, maxContamination);
        cleanCompletionThreshold = Mathf.Clamp(cleanCompletionThreshold, 0.01f, 1f);

        if (forceColliderAsTrigger && cleaningCollider != null)
            cleaningCollider.isTrigger = true;
    }

    public void ApplyWaterAtWorldPoint(
        Vector3 worldPoint,
        float contactRadius,
        float cleanAmount,
        float waterAmount,
        WaterQuality waterQuality,
        PlayerStatus cleaner = null)
    {
        SwimmingPoolObjective poolObjective =
            GetComponentInParent<SwimmingPoolObjective>();

        ForwardWaterToPoolDirtSpots(
            worldPoint,
            contactRadius,
            cleanAmount,
            waterAmount,
            waterQuality,
            cleaner,
            poolObjective);

        if (waterQuality == WaterQuality.Contaminated)
        {
            ApplyContaminatedWater(waterAmount);
            return;
        }

        if (poolObjective != null && poolObjective.IsCleaningLocked)
            return;

        Clean(cleanAmount);
    }

    void ForwardWaterToPoolDirtSpots(
        Vector3 worldPoint,
        float contactRadius,
        float cleanAmount,
        float waterAmount,
        WaterQuality waterQuality,
        PlayerStatus cleaner,
        SwimmingPoolObjective poolObjective)
    {
        if (!forwardWaterToPoolDirtSpots)
            return;

        if (poolObjective == null)
            return;

        float amount = waterQuality == WaterQuality.Contaminated
            ? waterAmount
            : cleanAmount;
        if (amount <= 0f)
            return;

        poolObjective.ApplyWaterAtWorldPoint(
            worldPoint,
            Mathf.Max(0.01f, contactRadius * forwardedDirtRadiusMultiplier),
            amount,
            waterQuality,
            cleaner);
    }

    public void Clean(float amount)
    {
        SwimmingPoolObjective poolObjective =
            GetComponentInParent<SwimmingPoolObjective>();
        if (poolObjective != null && poolObjective.IsCleaningLocked)
            return;

        if (IsCleaned || amount <= 0f)
            return;

        float previous = currentContamination;
        currentContamination = Mathf.Max(0f, currentContamination - amount);

        if (!Mathf.Approximately(previous, currentContamination))
            OnProgressChanged?.Invoke(this);

        RefreshCleanState(true);
    }

    public void ApplyContaminatedWater(float waterAmount)
    {
        if (!contaminatedWaterDirtiesPool || waterAmount <= 0f)
            return;

        float previous = currentContamination;
        currentContamination = Mathf.Min(
            maxContamination,
            currentContamination + waterAmount * contaminatedWaterPerDirt);

        if (IsCleaned && currentContamination > 0f)
            IsCleaned = false;

        if (!Mathf.Approximately(previous, currentContamination))
            OnProgressChanged?.Invoke(this);
    }

    void RefreshCleanState(bool notify)
    {
        bool cleanedNow = CleanPercent >= cleanCompletionThreshold;
        if (!cleanedNow || IsCleaned)
            return;

        IsCleaned = true;
        currentContamination = 0f;

        if (notify)
            OnCleaned?.Invoke(this);
    }

    void ResolveCollider()
    {
        if (cleaningCollider == null)
            cleaningCollider = GetComponent<Collider>();

        if (cleaningCollider == null)
            cleaningCollider = GetComponentInChildren<Collider>();

        if (forceColliderAsTrigger && cleaningCollider != null)
            cleaningCollider.isTrigger = true;
    }
}
