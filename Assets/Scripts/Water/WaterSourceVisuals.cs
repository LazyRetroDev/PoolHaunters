using UnityEngine;

[RequireComponent(typeof(WaterSourceDryable))]
public class WaterSourceVisuals : MonoBehaviour
{
    [Header("Source")]
    public WaterSourceDryable source;
    public float refreshInterval = 0.1f;

    [Header("Quality Colors")]
    public Color cleanColor = new Color(0.35f, 0.75f, 1f, 1f);
    public Color contaminatedColor = new Color(0.35f, 0.9f, 0.25f, 1f);
    public Color chemicallyEnhancedColor = new Color(0.65f, 0.95f, 1f, 1f);
    public Color dryColor = new Color(0.28f, 0.28f, 0.28f, 1f);
    public bool fadeToDryColorAsWaterLowers = true;
    [Range(0f, 1f)] public float dryColorBlendAtEmpty = 0.65f;
    public Renderer[] colorRenderers;

    [Header("Amount Scaling")]
    public Transform[] amountScaleTargets;
    public bool scaleOnlyY = true;
    [Range(0f, 1f)] public float minimumVisibleScale = 0.08f;
    public bool hideScaleTargetsWhenDry = false;
    public AnimationCurve amountToScale = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Particle Visuals")]
    public bool useSourceParticlesWhenEmpty = true;
    public bool autoFindChildParticles = false;
    public ParticleSystem[] particlesToTint;
    public ParticleSystem[] particlesToScaleWithAmount;
    public bool tintParticles = true;
    public bool scaleParticleEmission = true;
    public bool scaleParticleSize = false;
    public bool stopParticlesWhenDry = true;
    public bool playParticlesWhenWet = true;

    [Header("Optional Cue Objects")]
    public GameObject cleanCueRoot;
    public GameObject contaminatedCueRoot;
    public GameObject chemicallyEnhancedCueRoot;
    public GameObject lowWaterCueRoot;
    [Range(0f, 1f)] public float lowWaterThreshold = 0.25f;

    private const string BaseColorProperty = "_BaseColor";
    private const string ColorProperty = "_Color";

    private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
    private Vector3[] initialScales;
    private ParticleVisualState[] tintParticleStates;
    private ParticleVisualState[] amountParticleStates;
    private float refreshTimer;

    struct ParticleVisualState
    {
        public ParticleSystem particles;
        public float emissionRateMultiplier;
        public float startSizeMultiplier;
    }

    void Awake()
    {
        CacheSource();
        CacheScaleTargets();
        CacheParticles();
    }

    void OnEnable()
    {
        CacheSource();
        CacheScaleTargets();
        CacheParticles();
        ApplyVisuals();
    }

    void OnValidate()
    {
        CacheSource();
        refreshInterval = Mathf.Max(0f, refreshInterval);
        minimumVisibleScale = Mathf.Clamp01(minimumVisibleScale);
        lowWaterThreshold = Mathf.Clamp01(lowWaterThreshold);
        dryColorBlendAtEmpty = Mathf.Clamp01(dryColorBlendAtEmpty);
    }

    void Update()
    {
        refreshTimer -= Time.deltaTime;
        if (refreshTimer > 0f)
            return;

        refreshTimer = refreshInterval;
        ApplyVisuals();
    }

    void CacheSource()
    {
        if (source == null)
            source = GetComponent<WaterSourceDryable>();
    }

    void CacheScaleTargets()
    {
        int targetCount = amountScaleTargets != null ? amountScaleTargets.Length : 0;
        initialScales = new Vector3[targetCount];

        for (int i = 0; i < targetCount; i++)
        {
            initialScales[i] = amountScaleTargets[i] != null
                ? amountScaleTargets[i].localScale
                : Vector3.one;
        }
    }

    void CacheParticles()
    {
        if (useSourceParticlesWhenEmpty && source != null)
        {
            if ((particlesToTint == null || particlesToTint.Length == 0) && source.waterParticles != null)
                particlesToTint = source.waterParticles;

            if ((particlesToScaleWithAmount == null || particlesToScaleWithAmount.Length == 0) && source.waterParticles != null)
                particlesToScaleWithAmount = source.waterParticles;
        }

        if (autoFindChildParticles)
        {
            ParticleSystem[] childParticles = GetComponentsInChildren<ParticleSystem>(true);
            if (particlesToTint == null || particlesToTint.Length == 0)
                particlesToTint = childParticles;

            if (particlesToScaleWithAmount == null || particlesToScaleWithAmount.Length == 0)
                particlesToScaleWithAmount = childParticles;
        }

        tintParticleStates = BuildParticleStates(particlesToTint);
        amountParticleStates = BuildParticleStates(particlesToScaleWithAmount);
    }

    ParticleVisualState[] BuildParticleStates(ParticleSystem[] particleSystems)
    {
        int count = particleSystems != null ? particleSystems.Length : 0;
        ParticleVisualState[] states = new ParticleVisualState[count];

        for (int i = 0; i < count; i++)
        {
            ParticleSystem particles = particleSystems[i];
            states[i].particles = particles;

            if (particles == null)
                continue;

            ParticleSystem.MainModule main = particles.main;
            ParticleSystem.EmissionModule emission = particles.emission;

            states[i].emissionRateMultiplier = emission.rateOverTimeMultiplier;
            states[i].startSizeMultiplier = main.startSizeMultiplier;
        }

        return states;
    }

    void ApplyVisuals()
    {
        CacheSource();
        if (source == null)
            return;

        float waterPercent = Mathf.Clamp01(source.WaterPercent);
        bool hasWater = source.HasWater;
        Color qualityColor = GetDisplayColor(waterPercent, hasWater);

        ApplyRendererColors(qualityColor);
        ApplyScale(waterPercent, hasWater);
        ApplyParticleColors(qualityColor, hasWater);
        ApplyParticleAmount(waterPercent, hasWater);
        ApplyCueRoots(waterPercent, hasWater);
    }

    Color GetDisplayColor(float waterPercent, bool hasWater)
    {
        Color qualityColor = hasWater ? GetQualityColor(source.waterQuality) : dryColor;
        if (!hasWater || !fadeToDryColorAsWaterLowers)
            return qualityColor;

        float dryBlend = Mathf.Lerp(dryColorBlendAtEmpty, 0f, waterPercent);
        return Color.Lerp(qualityColor, dryColor, dryBlend);
    }

    Color GetQualityColor(WaterQuality quality)
    {
        switch (quality)
        {
            case WaterQuality.Contaminated:
                return contaminatedColor;
            case WaterQuality.ChemicallyEnhanced:
                return chemicallyEnhancedColor;
            default:
                return cleanColor;
        }
    }

    void ApplyRendererColors(Color color)
    {
        if (colorRenderers == null)
            return;

        for (int i = 0; i < colorRenderers.Length; i++)
        {
            Renderer targetRenderer = colorRenderers[i];
            if (targetRenderer == null)
                continue;

            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorProperty, color);
            propertyBlock.SetColor(ColorProperty, color);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }

    void ApplyScale(float waterPercent, bool hasWater)
    {
        if (amountScaleTargets == null)
            return;

        if (initialScales == null || initialScales.Length != amountScaleTargets.Length)
            CacheScaleTargets();

        float scalePercent = Mathf.Clamp01(amountToScale.Evaluate(waterPercent));
        if (hasWater)
            scalePercent = Mathf.Max(minimumVisibleScale, scalePercent);

        for (int i = 0; i < amountScaleTargets.Length; i++)
        {
            Transform target = amountScaleTargets[i];
            if (target == null)
                continue;

            bool showTarget = hasWater || !hideScaleTargetsWhenDry;
            if (target.gameObject.activeSelf != showTarget)
                target.gameObject.SetActive(showTarget);

            Vector3 baseScale = initialScales[i];
            target.localScale = scaleOnlyY
                ? new Vector3(baseScale.x, baseScale.y * scalePercent, baseScale.z)
                : baseScale * scalePercent;
        }
    }

    void ApplyParticleColors(Color color, bool hasWater)
    {
        if (!tintParticles || tintParticleStates == null)
            return;

        for (int i = 0; i < tintParticleStates.Length; i++)
        {
            ParticleSystem particles = tintParticleStates[i].particles;
            if (particles == null)
                continue;

            ParticleSystem.MainModule main = particles.main;
            main.startColor = color;

            UpdateParticlePlayState(particles, hasWater);
        }
    }

    void ApplyParticleAmount(float waterPercent, bool hasWater)
    {
        if (amountParticleStates == null)
            return;

        for (int i = 0; i < amountParticleStates.Length; i++)
        {
            ParticleSystem particles = amountParticleStates[i].particles;
            if (particles == null)
                continue;

            if (scaleParticleEmission)
            {
                ParticleSystem.EmissionModule emission = particles.emission;
                emission.rateOverTimeMultiplier = amountParticleStates[i].emissionRateMultiplier * waterPercent;
            }

            if (scaleParticleSize)
            {
                ParticleSystem.MainModule main = particles.main;
                main.startSizeMultiplier = amountParticleStates[i].startSizeMultiplier * Mathf.Max(minimumVisibleScale, waterPercent);
            }

            UpdateParticlePlayState(particles, hasWater);
        }
    }

    void UpdateParticlePlayState(ParticleSystem particles, bool hasWater)
    {
        if (particles == null)
            return;

        if (!hasWater && stopParticlesWhenDry)
        {
            if (particles.isPlaying)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        else if (hasWater && playParticlesWhenWet && !particles.isPlaying)
        {
            particles.Play();
        }
    }

    void ApplyCueRoots(float waterPercent, bool hasWater)
    {
        SetActiveIfAssigned(cleanCueRoot, hasWater && source.waterQuality == WaterQuality.Clean);
        SetActiveIfAssigned(contaminatedCueRoot, hasWater && source.waterQuality == WaterQuality.Contaminated);
        SetActiveIfAssigned(chemicallyEnhancedCueRoot, hasWater && source.waterQuality == WaterQuality.ChemicallyEnhanced);
        SetActiveIfAssigned(lowWaterCueRoot, hasWater && waterPercent <= lowWaterThreshold);
    }

    void SetActiveIfAssigned(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
