using UnityEngine;
using UnityEngine.UI;

public class PlayerVignetteEffect : MonoBehaviour
{
    [Header("References")]
    public PlayerStatus playerStatus;
    public Canvas targetCanvas;

    [Header("Look")]
    public Color vignetteColor = new Color(0.02f, 0f, 0f, 1f);
    [Range(0f, 1f)] public float baseIntensity = 0.12f;
    [Range(0f, 1f)] public float maxIntensity = 0.8f;
    [Range(0f, 1f)] public float centerClearRadius = 0.34f;
    [Range(0.1f, 8f)] public float edgeSoftness = 2.4f;
    public int textureSize = 256;

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

    private RawImage vignetteImage;
    private Texture2D vignetteTexture;
    private float externalThreatIntensity;
    private float pulseIntensity;
    private float pulseTimer;
    private float currentIntensity;
    private float previousHealthPercent = 1f;

    void Awake()
    {
        if (playerStatus == null)
            playerStatus = GetComponentInParent<PlayerStatus>();

        CreateOverlayIfNeeded();
        BuildVignetteTexture();
    }

    void Start()
    {
        if (playerStatus != null)
            previousHealthPercent = playerStatus.GetHealthPercent();
    }

    void Update()
    {
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
        pulseIntensity = Mathf.Max(pulseIntensity, Mathf.Clamp01(intensity));
        pulseTimer = Mathf.Max(pulseTimer, duration);
    }

    void CreateOverlayIfNeeded()
    {
        if (targetCanvas == null)
        {
            GameObject canvasObject = new GameObject("Player Vignette Canvas");
            canvasObject.transform.SetParent(transform, false);

            targetCanvas = canvasObject.AddComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = 200;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
        }

        Transform existing = targetCanvas.transform.Find("Player Vignette Overlay");
        if (existing != null)
        {
            vignetteImage = existing.GetComponent<RawImage>();
            return;
        }

        GameObject imageObject = new GameObject("Player Vignette Overlay");
        imageObject.transform.SetParent(targetCanvas.transform, false);

        vignetteImage = imageObject.AddComponent<RawImage>();
        vignetteImage.raycastTarget = false;

        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void BuildVignetteTexture()
    {
        int size = Mathf.Max(32, textureSize);
        vignetteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        vignetteTexture.name = "Generated Player Vignette";
        vignetteTexture.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxDistance = center.magnitude;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center) / maxDistance;
                float alpha = Mathf.InverseLerp(centerClearRadius, 1f, distance);
                alpha = Mathf.Pow(Mathf.Clamp01(alpha), edgeSoftness);
                vignetteTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        vignetteTexture.Apply();

        if (vignetteImage != null)
            vignetteImage.texture = vignetteTexture;
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
            return;
        }

        pulseTimer -= Time.deltaTime;
        float normalizedTime = damagePulseDuration > 0f ? pulseTimer / damagePulseDuration : 0f;
        pulseIntensity = Mathf.Clamp01(pulseIntensity * Mathf.Clamp01(normalizedTime));
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
            float pulse = (Mathf.Sin(Time.time * breathingSpeed) + 1f) * 0.5f;
            targetIntensity += pulse * breathingAmount * targetIntensity;
        }

        targetIntensity = Mathf.Clamp(targetIntensity, 0f, maxIntensity);
        float speed = targetIntensity > currentIntensity ? fadeInSpeed : fadeOutSpeed;
        currentIntensity = Mathf.MoveTowards(currentIntensity, targetIntensity, speed * Time.deltaTime);

        if (vignetteImage != null)
            vignetteImage.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, currentIntensity);
    }

    void OnValidate()
    {
        textureSize = Mathf.Max(32, textureSize);
        lowHealthStartsAt = Mathf.Max(0.01f, lowHealthStartsAt);
        damagePulseDuration = Mathf.Max(0.01f, damagePulseDuration);
    }
}
