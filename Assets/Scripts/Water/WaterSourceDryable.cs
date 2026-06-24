using UnityEngine;

public class WaterSourceDryable : MonoBehaviour
{
    [Header("State")]
    public bool startsDry = false;
    public bool isDry;

    [Header("Water Supply")]
    public WaterQuality waterQuality = WaterQuality.Clean;
    public float maxWaterAmount = 250f;
    public float currentWaterAmount = 250f;
    public bool refillToMaxOnStart = true;
    public bool replacePlayerWaterQuality = false;

    [Header("Deterioration")]
    public bool deterioratesOverTime = true;
    public float waterLossPerSecond = 0.25f;
    public bool canBecomeContaminated = true;
    public float contaminationDelay = 120f;

    [Header("Visuals")]
    public GameObject wetVisualRoot;
    public GameObject dryVisualRoot;
    public ParticleSystem[] waterParticles;
    public Renderer[] renderersToDisableWhenDry;
    public Collider[] collidersToDisableWhenDry;

    [Header("Optional")]
    public bool disableObjectWhenDry = false;
    public GameObject[] extraObjectsToDisableWhenDry;

    private float contaminationTimer;

    public float WaterPercent => maxWaterAmount > 0f ? currentWaterAmount / maxWaterAmount : 0f;
    public bool HasWater => !isDry && currentWaterAmount > 0f;

    void Start()
    {
        if (refillToMaxOnStart)
            currentWaterAmount = maxWaterAmount;

        currentWaterAmount = Mathf.Clamp(currentWaterAmount, 0f, maxWaterAmount);
        contaminationTimer = contaminationDelay;
        isDry = startsDry || currentWaterAmount <= 0f;
        UpdateDryState();
    }

    void Update()
    {
        if (isDry) return;

        UpdateDeterioration();
        UpdateContaminationTimer();
    }

    void UpdateDeterioration()
    {
        if (!deterioratesOverTime || waterLossPerSecond <= 0f) return;
        DrainWater(waterLossPerSecond * Time.deltaTime, out _);
    }

    void UpdateContaminationTimer()
    {
        if (!canBecomeContaminated || contaminationDelay <= 0f) return;
        if (waterQuality == WaterQuality.Contaminated) return;

        contaminationTimer -= Time.deltaTime;
        if (contaminationTimer <= 0f)
            Contaminate();
    }

    public float DrainWater(float requestedAmount, out WaterQuality drainedQuality)
    {
        drainedQuality = waterQuality;
        if (isDry || requestedAmount <= 0f || currentWaterAmount <= 0f) return 0f;

        float drainedAmount = Mathf.Min(requestedAmount, currentWaterAmount);
        currentWaterAmount -= drainedAmount;

        if (currentWaterAmount <= 0f)
        {
            currentWaterAmount = 0f;
            DryOut();
        }

        return drainedAmount;
    }

    public void Contaminate()
    {
        if (isDry) return;
        waterQuality = WaterQuality.Contaminated;
    }

    public void SetQuality(WaterQuality quality)
    {
        waterQuality = quality;
    }

    public void DryOut()
    {
        if (isDry) return;

        isDry = true;
        currentWaterAmount = 0f;
        UpdateDryState();
    }

    public void RestoreWater()
    {
        if (!isDry && currentWaterAmount > 0f) return;

        isDry = false;
        if (currentWaterAmount <= 0f)
            currentWaterAmount = maxWaterAmount;

        contaminationTimer = contaminationDelay;
        UpdateDryState();
    }

    void UpdateDryState()
    {
        if (wetVisualRoot != null)
            wetVisualRoot.SetActive(!isDry);

        if (dryVisualRoot != null)
            dryVisualRoot.SetActive(isDry);

        for (int i = 0; i < waterParticles.Length; i++)
        {
            if (waterParticles[i] == null) continue;

            if (isDry)
                waterParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            else
                waterParticles[i].Play();
        }

        for (int i = 0; i < renderersToDisableWhenDry.Length; i++)
        {
            if (renderersToDisableWhenDry[i] != null)
                renderersToDisableWhenDry[i].enabled = !isDry;
        }

        for (int i = 0; i < collidersToDisableWhenDry.Length; i++)
        {
            if (collidersToDisableWhenDry[i] != null)
                collidersToDisableWhenDry[i].enabled = !isDry;
        }

        for (int i = 0; i < extraObjectsToDisableWhenDry.Length; i++)
        {
            if (extraObjectsToDisableWhenDry[i] != null)
                extraObjectsToDisableWhenDry[i].SetActive(!isDry);
        }

        if (disableObjectWhenDry && isDry)
            gameObject.SetActive(false);
    }
}
