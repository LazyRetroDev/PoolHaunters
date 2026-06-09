using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PlayerVignetteEffect : MonoBehaviour
{
    [Header("References")]
    public PlayerStatus playerStatus;
    public Component targetCinemachineVolumeSettings;
    public Volume targetVolume;
    public bool preferCinemachineVolumeSettings = true;
    public bool findVolumeAutomatically = true;
    public bool createLocalVolumeIfMissing = true;

    [Header("Look")]
    public Color vignetteColor = new Color(0.02f, 0f, 0f, 1f);
    [Range(0f, 1f)] public float baseIntensity = 0.12f;
    [Range(0f, 1f)] public float maxIntensity = 0.8f;
    [Range(0f, 1f)] public float smoothness = 0.55f;
    public bool rounded = false;

    [Header("Low Health")]
    public bool reactToLowHealth = true;
    [Range(0f, 1f)] public float lowHealthStartsAt = 0.45f;
    [Range(0f, 1f)] public float lowHealthIntensity = 0.45f;

    [Header("Damage Pulse")]
    public bool pulseWhenDamaged = true;
    [Range(0f, 1f)] public float damagePulseIntensity = 0.65f;
    public float damagePulseDuration = 0.35f;

    [Header("Horror Breathing")]
    public bool useBreathingPulse = true;
    [Range(0f, 1f)] public float breathingAmount = 0.08f;
    public float breathingSpeed = 1.25f;

    [Header("Smoothing")]
    public float fadeInSpeed = 8f;
    public float fadeOutSpeed = 4f;

    private Vignette vignette;
    private VolumeProfile activeProfile;
    private float externalThreatIntensity;
    private float pulseIntensity;
    private float pulseStartIntensity;
    private float pulseTimer;
    private float pulseDuration;
    private float currentIntensity;
    private float previousHealthPercent = 1f;

    void Awake()
    {
        if (playerStatus == null)
            playerStatus = GetComponentInParent<PlayerStatus>();

        ResolveVignette();
    }

    void Start()
    {
        if (playerStatus != null)
            previousHealthPercent = playerStatus.GetHealthPercent();
    }

    void Update()
    {
        if (vignette == null)
            ResolveVignette();

        DetectDamagePulse();
        UpdatePulse();
        UpdateIntensity();
    }

    public void SetThreatIntensity(float intensity)
    {
        externalThreatIntensity = Mathf.Clamp01(intensity);
    }

    public void ClearThreatIntensity()
    {
        externalThreatIntensity = 0f;
    }

    public void Pulse(float intensity, float duration)
    {
        pulseStartIntensity = Mathf.Max(pulseStartIntensity, Mathf.Clamp01(intensity));
        pulseIntensity = pulseStartIntensity;
        pulseDuration = Mathf.Max(0.01f, duration);
        pulseTimer = Mathf.Max(pulseTimer, pulseDuration);
    }

    void ResolveVignette()
    {
        VolumeProfile profile = null;

        if (preferCinemachineVolumeSettings)
            profile = ResolveCinemachineVolumeProfile();

        if (profile == null)
            profile = ResolveUnityVolumeProfile();

        if (profile == null) return;
        activeProfile = profile;

        if (!activeProfile.TryGet(out vignette))
            vignette = activeProfile.Add<Vignette>(true);

        vignette.active = true;
        vignette.color.overrideState = true;
        vignette.intensity.overrideState = true;
        vignette.smoothness.overrideState = true;
        vignette.rounded.overrideState = true;
    }

    VolumeProfile ResolveCinemachineVolumeProfile()
    {
        ResolveCinemachineVolumeSettings();
        if (targetCinemachineVolumeSettings == null) return null;

        VolumeProfile profile = GetProfileFromCinemachineVolumeSettings(targetCinemachineVolumeSettings);
        if (profile != null) return profile;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        SetProfileOnCinemachineVolumeSettings(targetCinemachineVolumeSettings, profile);
        return GetProfileFromCinemachineVolumeSettings(targetCinemachineVolumeSettings);
    }

    void ResolveCinemachineVolumeSettings()
    {
        if (targetCinemachineVolumeSettings != null) return;

        targetCinemachineVolumeSettings = GetComponent("CinemachineVolumeSettings");
        if (targetCinemachineVolumeSettings == null)
            targetCinemachineVolumeSettings = GetComponentInChildrenByName("CinemachineVolumeSettings");
        if (targetCinemachineVolumeSettings == null)
            targetCinemachineVolumeSettings = GetComponentInParentByName("CinemachineVolumeSettings");
    }

    Component GetComponentInChildrenByName(string typeName)
    {
        Component[] components = GetComponentsInChildren<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != null && components[i].GetType().Name == typeName)
                return components[i];
        }

        return null;
    }

    Component GetComponentInParentByName(string typeName)
    {
        Component[] components = GetComponentsInParent<Component>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != null && components[i].GetType().Name == typeName)
                return components[i];
        }

        return null;
    }

    VolumeProfile GetProfileFromCinemachineVolumeSettings(Component volumeSettings)
    {
        if (volumeSettings == null) return null;

        object value = GetMemberValue(volumeSettings, "Profile");
        if (value is VolumeProfile profile)
            return profile;

        return null;
    }

    void SetProfileOnCinemachineVolumeSettings(Component volumeSettings, VolumeProfile profile)
    {
        if (volumeSettings == null || profile == null) return;

        TypeMemberAccessor accessor = GetMemberAccessor(volumeSettings, "Profile");
        if (accessor.property != null && accessor.property.CanWrite)
            accessor.property.SetValue(volumeSettings, profile, null);
        else if (accessor.field != null && !accessor.field.IsInitOnly)
            accessor.field.SetValue(volumeSettings, profile);
    }

    object GetMemberValue(object target, string memberName)
    {
        TypeMemberAccessor accessor = GetMemberAccessor(target, memberName);
        if (accessor.property != null)
            return accessor.property.GetValue(target, null);
        if (accessor.field != null)
            return accessor.field.GetValue(target);

        return null;
    }

    TypeMemberAccessor GetMemberAccessor(object target, string memberName)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        System.Type type = target.GetType();

        return new TypeMemberAccessor
        {
            property = type.GetProperty(memberName, flags),
            field = type.GetField(memberName, flags)
        };
    }

    VolumeProfile ResolveUnityVolumeProfile()
    {
        ResolveVolume();
        if (targetVolume == null) return null;

        VolumeProfile profile = targetVolume.profile;
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            targetVolume.profile = profile;
        }

        return profile;
    }

    void ResolveVolume()
    {
        if (targetVolume != null) return;

        if (findVolumeAutomatically)
        {
            targetVolume = GetComponentInChildren<Volume>();
            if (targetVolume == null)
                targetVolume = GetComponentInParent<Volume>();
            if (targetVolume == null)
                targetVolume = FindObjectOfType<Volume>();
        }

        if (targetVolume == null && createLocalVolumeIfMissing)
            targetVolume = CreateLocalVolume();
    }

    Volume CreateLocalVolume()
    {
        GameObject volumeObject = new GameObject("Player Vignette Volume");
        volumeObject.transform.SetParent(transform, false);

        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        return volume;
    }

    void DetectDamagePulse()
    {
        if (!pulseWhenDamaged || playerStatus == null) return;

        float healthPercent = playerStatus.GetHealthPercent();
        if (healthPercent < previousHealthPercent - 0.001f)
            Pulse(damagePulseIntensity, damagePulseDuration);

        previousHealthPercent = healthPercent;
    }

    void UpdatePulse()
    {
        if (pulseTimer <= 0f)
        {
            pulseIntensity = 0f;
            pulseStartIntensity = 0f;
            return;
        }

        pulseTimer -= Time.deltaTime;
        float normalizedTime = pulseDuration > 0f ? pulseTimer / pulseDuration : 0f;
        pulseIntensity = pulseStartIntensity * Mathf.Clamp01(normalizedTime);
    }

    void UpdateIntensity()
    {
        float targetIntensity = baseIntensity;
        targetIntensity = Mathf.Max(targetIntensity, externalThreatIntensity);
        targetIntensity = Mathf.Max(targetIntensity, pulseIntensity);

        if (reactToLowHealth && playerStatus != null)
        {
            float healthPercent = playerStatus.GetHealthPercent();
            float lowHealthT = Mathf.InverseLerp(lowHealthStartsAt, 0f, healthPercent);
            targetIntensity = Mathf.Max(targetIntensity, lowHealthT * lowHealthIntensity);
        }

        if (useBreathingPulse && targetIntensity > 0.001f)
        {
            float breathingT = (Mathf.Sin(Time.time * breathingSpeed) + 1f) * 0.5f;
            targetIntensity += breathingT * breathingAmount * targetIntensity;
        }

        targetIntensity = Mathf.Clamp(targetIntensity, 0f, maxIntensity);
        float speed = targetIntensity > currentIntensity ? fadeInSpeed : fadeOutSpeed;
        currentIntensity = Mathf.MoveTowards(currentIntensity, targetIntensity, speed * Time.deltaTime);

        ApplyVignette(currentIntensity);
    }

    void ApplyVignette(float intensity)
    {
        if (vignette == null) return;

        vignette.color.Override(vignetteColor);
        vignette.intensity.Override(intensity);
        vignette.smoothness.Override(smoothness);
        vignette.rounded.Override(rounded);
    }

    void OnValidate()
    {
        lowHealthStartsAt = Mathf.Max(0.01f, lowHealthStartsAt);
        damagePulseDuration = Mathf.Max(0.01f, damagePulseDuration);
        pulseDuration = Mathf.Max(0.01f, pulseDuration);
    }

    struct TypeMemberAccessor
    {
        public PropertyInfo property;
        public FieldInfo field;
    }
}
