using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-5000)]
public sealed class MapGenerationLoadingScreen : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField, Min(0f)] private float minimumVisibleSeconds = 0.5f;
    [SerializeField, Min(0f)] private float readyHoldSeconds = 0.15f;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.25f;

    [Header("Input")]
    [SerializeField] private bool lockLocalPlayerInput = true;

    private RoomGenerator roomGenerator;
    private CanvasGroup canvasGroup;
    private TMP_Text statusText;
    private RectTransform activityIndicator;
    private float visibleSince;
    private int displayedDotCount = -1;
    private bool isVisible;
    private bool hideStarted;

    private void Awake()
    {
        roomGenerator = GetComponent<RoomGenerator>();
        BuildInterface();
        ShowImmediately();
    }

    private void OnEnable()
    {
        RoomGenerator.OnGeneratedMapReady += HandleGeneratedMapReady;
        GameLocalization.LanguageChanged += HandleLanguageChanged;
    }

    private void Start()
    {
        if (roomGenerator != null && roomGenerator.IsGeneratedMapReady)
            BeginHide();
    }

    private void OnDisable()
    {
        RoomGenerator.OnGeneratedMapReady -= HandleGeneratedMapReady;
        GameLocalization.LanguageChanged -= HandleLanguageChanged;
    }

    private void Update()
    {
        if (!isVisible)
            return;

        AnimateActivityIndicator();
        UpdateStatusText();

        if (lockLocalPlayerInput)
            SetLocalPlayerInput(false);
    }

    private void HandleGeneratedMapReady(RoomGenerator readyGenerator)
    {
        if (readyGenerator == roomGenerator)
            BeginHide();
    }

    private void HandleLanguageChanged(GameLanguage language)
    {
        displayedDotCount = -1;
        UpdateStatusText();
    }

    private void ShowImmediately()
    {
        if (canvasGroup == null)
            return;

        StopAllCoroutines();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.gameObject.SetActive(true);
        visibleSince = Time.unscaledTime;
        displayedDotCount = -1;
        isVisible = true;
        hideStarted = false;
        UpdateStatusText();
    }

    private void BeginHide()
    {
        if (!isVisible || hideStarted)
            return;

        hideStarted = true;
        StartCoroutine(HideRoutine());
    }

    private IEnumerator HideRoutine()
    {
        float earliestHideTime = visibleSince + minimumVisibleSeconds;
        while (Time.unscaledTime < earliestHideTime)
            yield return null;

        if (readyHoldSeconds > 0f)
            yield return new WaitForSecondsRealtime(readyHoldSeconds);

        float elapsed = 0f;
        float initialAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;

        while (canvasGroup != null && elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(
                initialAlpha,
                0f,
                Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.gameObject.SetActive(false);
        }

        isVisible = false;

        if (lockLocalPlayerInput)
            SetLocalPlayerInput(true);
    }

    private void AnimateActivityIndicator()
    {
        if (activityIndicator == null)
            return;

        float normalized = Mathf.PingPong(Time.unscaledTime * 0.7f, 1f);
        activityIndicator.anchoredPosition = new Vector2(
            Mathf.Lerp(-255f, 255f, normalized),
            0f);
    }

    private void UpdateStatusText()
    {
        if (statusText == null)
            return;

        int dotCount = Mathf.FloorToInt(Time.unscaledTime * 2f) % 4;
        if (dotCount == displayedDotCount)
            return;

        displayedDotCount = dotCount;
        string key = RegionRunState.IsClient
            ? "loading.syncMap"
            : "loading.generateMap";
        string fallback = RegionRunState.IsClient
            ? "Synchronizing map"
            : "Generating map";

        statusText.text = GameLocalization.Translate(key, fallback) +
            new string('.', dotCount);
    }

    private static void SetLocalPlayerInput(bool acceptsInput)
    {
        PlayerMovement[] movements =
            FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);

        for (int i = 0; i < movements.Length; i++)
        {
            PlayerMovement movement = movements[i];
            if (movement == null)
                continue;
            if (movement.IsSpawned && !movement.IsOwner)
                continue;

            movement.SetAcceptsInput(acceptsInput);
        }
    }

    private void BuildInterface()
    {
        GameObject canvasObject = CreateRect("Map Generation Loading Canvas", transform);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        canvasGroup = canvasObject.AddComponent<CanvasGroup>();

        GameObject background = CreateRect("Background", canvasObject.transform);
        Stretch(background.GetComponent<RectTransform>());
        Image backgroundImage = background.AddComponent<Image>();
        backgroundImage.color = new Color(0.025f, 0.035f, 0.03f, 1f);

        GameObject accent = CreateRect("Accent", background.transform);
        RectTransform accentRect = accent.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.sizeDelta = new Vector2(0f, 5f);
        Image accentImage = accent.AddComponent<Image>();
        accentImage.color = new Color(0.08f, 0.82f, 0.66f, 1f);

        TMP_Text title = CreateText(
            "Title",
            background.transform,
            "POOL HAUNTERS",
            42f,
            FontStyles.Bold,
            new Color(0.92f, 0.97f, 0.94f, 1f));
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0f, 42f);
        titleRect.sizeDelta = new Vector2(700f, 70f);

        statusText = CreateText(
            "Status",
            background.transform,
            string.Empty,
            23f,
            FontStyles.Normal,
            new Color(0.7f, 0.78f, 0.73f, 1f));
        RectTransform statusRect = statusText.rectTransform;
        statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.anchoredPosition = new Vector2(0f, -12f);
        statusRect.sizeDelta = new Vector2(600f, 45f);

        GameObject track = CreateRect("Activity Track", background.transform);
        RectTransform trackRect = track.GetComponent<RectTransform>();
        trackRect.anchorMin = trackRect.anchorMax = new Vector2(0.5f, 0.5f);
        trackRect.anchoredPosition = new Vector2(0f, -70f);
        trackRect.sizeDelta = new Vector2(420f, 7f);
        Image trackImage = track.AddComponent<Image>();
        trackImage.color = new Color(0.13f, 0.17f, 0.15f, 1f);
        track.AddComponent<RectMask2D>();

        GameObject indicator = CreateRect("Activity Indicator", track.transform);
        activityIndicator = indicator.GetComponent<RectTransform>();
        activityIndicator.anchorMin = activityIndicator.anchorMax =
            new Vector2(0.5f, 0.5f);
        activityIndicator.sizeDelta = new Vector2(110f, 7f);
        Image indicatorImage = indicator.AddComponent<Image>();
        indicatorImage.color = new Color(0.2f, 0.95f, 0.67f, 1f);
    }

    private static TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        float fontSize,
        FontStyles style,
        Color color)
    {
        GameObject textObject = CreateRect(objectName, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateRect(string objectName, Transform parent)
    {
        GameObject result = new GameObject(objectName, typeof(RectTransform));
        result.layer = 5;
        result.transform.SetParent(parent, false);
        return result;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
