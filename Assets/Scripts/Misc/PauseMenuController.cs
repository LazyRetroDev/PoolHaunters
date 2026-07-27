using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseMenuController : MonoBehaviour
{
    const string PrefPrefix = "PoolHaunters.Settings.";

    [Header("Scenes")]
    public string mainMenuSceneName = "Menu";

    [Header("Pause")]
    public bool pauseSinglePlayerTime = true;
    public bool lockPlayerWhilePaused = true;
    public PlayerStatus playerStatus;
    public CursorLockController cursorLockController;

    [Header("Optional Presentation")]
    public CanvasScaler[] hudScalers;
    public Transform rightHandToolRoot;
    public Transform leftHandToolRoot;
    public Transform rightSideItemsRoot;
    public Transform leftSideItemsRoot;

    private Canvas canvas;
    private GameObject mainPanel;
    private GameObject settingsPanel;
    private GameObject videoPage;
    private GameObject inputPage;
    private GameObject audioPage;
    private GameObject playerPage;
    private GameObject accessibilityPage;
    private TMP_Text fpsText;
    private Image brightnessOverlay;

    private CursorLockMode previousLockState;
    private bool previousCursorVisible;
    private bool previousCursorLockControllerEnabled;
    private bool paused;
    private bool controlLocked;
    private float previousTimeScale = 1f;
    private float fpsTimer;
    private int fpsFrames;

    static readonly int[] FpsCaps = { -1, 30, 60, 90, 120, 144, 165, 240 };
    static readonly Vector2Int[] CommonResolutions =
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1366, 768),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1440),
        new Vector2Int(3840, 2160)
    };

    public static float EffectScale { get; private set; } = 1f;
    public static float SoundEffectsVolume { get; private set; } = 1f;
    public static float MusicVolume { get; private set; } = 1f;
    public static float PlayerVoiceVolume { get; private set; } = 1f;
    public static float EnemySoundsVolume { get; private set; } = 1f;
    public static bool SubtitlesEnabled { get; private set; } = true;
    public static bool LeftHandedMode { get; private set; }
    public static int ColorblindMode { get; private set; }
    public static bool ReduceFlashing { get; private set; }
    public static bool ReduceCameraShake { get; private set; }

    void Awake()
    {
        ResolveReferences();
        EnsureCanvas();
        ApplySavedSettings();
        ShowMainPanel();
        canvas.gameObject.SetActive(false);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            TogglePause();

        UpdateFpsCounter();

        if (paused)
            KeepCursorUnlocked();
    }

    void OnDisable()
    {
        if (paused)
            ResumeGame();
    }

    public void TogglePause()
    {
        if (paused)
            ResumeGame();
        else
            PauseGame();
    }

    public void PauseGame()
    {
        if (paused)
            return;

        ResolveReferences();
        paused = true;
        previousTimeScale = Time.timeScale;

        if (pauseSinglePlayerTime && !IsMultiplayerSessionRunning())
            Time.timeScale = 0f;

        previousLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        previousCursorLockControllerEnabled = cursorLockController != null && cursorLockController.enabled;

        if (cursorLockController != null)
            cursorLockController.enabled = false;

        if (lockPlayerWhilePaused && playerStatus != null)
        {
            playerStatus.AddExternalControlLock();
            controlLocked = true;
        }

        KeepCursorUnlocked();
        canvas.gameObject.SetActive(true);
        ShowMainPanel();
    }

    public void ResumeGame()
    {
        if (!paused)
            return;

        paused = false;
        Time.timeScale = previousTimeScale;

        if (controlLocked && playerStatus != null)
            playerStatus.RemoveExternalControlLock();

        controlLocked = false;

        if (cursorLockController != null)
            cursorLockController.enabled = previousCursorLockControllerEnabled;

        Cursor.lockState = previousLockState;
        Cursor.visible = previousCursorVisible;

        if (canvas != null)
            canvas.gameObject.SetActive(false);
    }

    public void SelfDestruct()
    {
        if (playerStatus == null)
            ResolveReferences();

        if (playerStatus != null)
            playerStatus.TakeDamage(playerStatus.GetMaxHealth() * 10f);

        ResumeGame();
    }

    public void LoadMainMenu()
    {
        Time.timeScale = 1f;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
            networkManager.Shutdown();

        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;
        Application.Quit();
    }

    void ResolveReferences()
    {
        if (playerStatus == null)
            playerStatus = FindFirstObjectByType<PlayerStatus>();

        if (cursorLockController == null && playerStatus != null)
            cursorLockController = playerStatus.GetComponent<CursorLockController>();
    }

    bool IsMultiplayerSessionRunning()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening;
    }

    void KeepCursorUnlocked()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void EnsureCanvas()
    {
        if (canvas != null)
            return;

        EnsureEventSystem();

        GameObject canvasObject = new GameObject("Pause Menu Canvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        canvasObject.AddComponent<GraphicRaycaster>();

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildUi(canvasObject.transform);
    }

    void BuildUi(Transform root)
    {
        RectTransform background = CreateImage(
            "Pause Background",
            root,
            new Color(0.01f, 0.012f, 0.015f, 0.9f),
            Vector2.zero,
            Vector2.one);

        mainPanel = CreatePanel("Pause Panel", background, new Vector2(0.04f, 0.2f), new Vector2(0.31f, 0.82f));
        settingsPanel = CreatePanel("Settings Panel", background, new Vector2(0.04f, 0.08f), new Vector2(0.56f, 0.92f));

        CreateTitle(mainPanel.transform, "PAUSED");
        CreateButton(mainPanel.transform, "CONTINUE", ResumeGame);
        CreateButton(mainPanel.transform, "SETTINGS", ShowSettingsPanel);
        CreateButton(mainPanel.transform, "SELF-DESTRUCT", SelfDestruct);
        CreateButton(mainPanel.transform, "MAIN MENU", LoadMainMenu);
        CreateButton(mainPanel.transform, "QUIT GAME", QuitGame);

        BuildSettingsPanel(settingsPanel.transform);

        fpsText = CreateFloatingText(root, "FPS", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -18f));
        fpsText.gameObject.SetActive(PlayerPrefs.GetInt(PrefPrefix + "FpsCounter", 0) == 1);

        brightnessOverlay = CreateImage(
            "Brightness Overlay",
            root,
            Color.clear,
            Vector2.zero,
            Vector2.one).GetComponent<Image>();
        brightnessOverlay.raycastTarget = false;
    }

    void BuildSettingsPanel(Transform root)
    {
        CreateTitle(root, "SETTINGS");

        GameObject body = CreateHorizontalGroup("Settings Body", root, 16f);
        AddLayout(body, 600f, 1f);

        GameObject tabs = CreateVerticalGroup("Tabs", body.transform, 8f);
        AddLayout(tabs, 600f, 1f);
        SetLayoutWidth(tabs, 190f);

        GameObject pageRoot = CreateVerticalGroup("Settings Pages", body.transform, 8f);
        AddLayout(pageRoot, 600f, 1f);
        SetLayoutWidth(pageRoot, 560f, 1f);

        CreateButton(tabs.transform, "VIDEO", () => ShowSettingsPage(videoPage));
        CreateButton(tabs.transform, "INPUT", () => ShowSettingsPage(inputPage));
        CreateButton(tabs.transform, "AUDIO", () => ShowSettingsPage(audioPage));
        CreateButton(tabs.transform, "PLAYER", () => ShowSettingsPage(playerPage));
        CreateButton(tabs.transform, "ACCESS", () => ShowSettingsPage(accessibilityPage));

        videoPage = CreateSettingsPage(pageRoot.transform, "Video Page");
        inputPage = CreateSettingsPage(pageRoot.transform, "Input Page");
        audioPage = CreateSettingsPage(pageRoot.transform, "Audio Page");
        playerPage = CreateSettingsPage(pageRoot.transform, "Player Page");
        accessibilityPage = CreateSettingsPage(pageRoot.transform, "Accessibility Page");

        BuildVideoPage(videoPage.transform);
        BuildInputPage(inputPage.transform);
        BuildAudioPage(audioPage.transform);
        BuildPlayerPage(playerPage.transform);
        BuildAccessibilityPage(accessibilityPage.transform);

        CreateButton(root, "BACK", ShowMainPanel);
        ShowSettingsPage(videoPage);
    }

    void BuildVideoPage(Transform root)
    {
        CreateToggle(root, "V-SYNC", PrefPrefix + "VSync", QualitySettings.vSyncCount > 0, value =>
        {
            QualitySettings.vSyncCount = value ? 1 : 0;
            PlayerPrefs.SetInt(PrefPrefix + "VSync", value ? 1 : 0);
        });

        CreateToggle(root, "FPS COUNT", PrefPrefix + "FpsCounter", false, value =>
        {
            if (fpsText != null)
                fpsText.gameObject.SetActive(value);
            PlayerPrefs.SetInt(PrefPrefix + "FpsCounter", value ? 1 : 0);
        });

        CreateCycleControl(root, "FPS CAP", BuildFpsOptions(), GetSavedFpsIndex(), index =>
        {
            int cap = FpsCaps[Mathf.Clamp(index, 0, FpsCaps.Length - 1)];
            Application.targetFrameRate = cap;
            PlayerPrefs.SetInt(PrefPrefix + "FpsCap", cap);
        });

        CreateCycleControl(root, "GRAPHICS", new List<string> { "Potato", "Low", "Medium", "High" }, GetSavedQualityIndex(), index =>
        {
            ApplyQualityPreset(index);
            PlayerPrefs.SetInt(PrefPrefix + "Quality", index);
        });

        CreateSlider(root, "HUD SCALE", PrefPrefix + "HudScale", 1f, 0.75f, 1.5f, ApplyHudScale);
        CreateCycleControl(root, "DISPLAY", new List<string> { "Fullscreen", "Borderless Windowed", "Windowed" }, GetSavedDisplayModeIndex(), ApplyDisplayMode);
        CreateCycleControl(root, "RESOLUTION", BuildResolutionOptions(), GetSavedResolutionIndex(), ApplyResolution);
        CreateSlider(root, "EFFECT SCALE", PrefPrefix + "EffectScale", 1f, 0.25f, 1f, value => EffectScale = value);
        CreateSlider(root, "BRIGHTNESS", PrefPrefix + "Brightness", 1f, 0.5f, 1.5f, ApplyBrightness);
    }

    void BuildInputPage(Transform root)
    {
        CreateText(root, "Input remapping and controller binding hooks are reserved here.");
        CreateToggle(root, "CONTROLLER PROMPTS", PrefPrefix + "ControllerPrompts", false, value => { });
        CreateCycleControl(root, "CONTROLLER STYLE", new List<string> { "Auto", "Xbox", "PlayStation", "Keyboard" }, PlayerPrefs.GetInt(PrefPrefix + "ControllerStyle", 0), index =>
        {
            PlayerPrefs.SetInt(PrefPrefix + "ControllerStyle", index);
        });
    }

    void BuildAudioPage(Transform root)
    {
        CreateSlider(root, "MASTER", PrefPrefix + "MasterVolume", 1f, 0f, 1f, value =>
        {
            AudioListener.volume = value;
        });
        CreateSlider(root, "SOUND EFFECTS", PrefPrefix + "SfxVolume", 1f, 0f, 1f, value => SoundEffectsVolume = value);
        CreateSlider(root, "MUSIC", PrefPrefix + "MusicVolume", 1f, 0f, 1f, value => MusicVolume = value);
        CreateSlider(root, "PLAYER VOICE", PrefPrefix + "PlayerVoiceVolume", 1f, 0f, 1f, value => PlayerVoiceVolume = value);
        CreateSlider(root, "ENEMY SOUNDS", PrefPrefix + "EnemySoundsVolume", 1f, 0f, 1f, value => EnemySoundsVolume = value);
        CreateToggle(root, "SUBTITLES", PrefPrefix + "Subtitles", true, value => SubtitlesEnabled = value);
        CreateCycleControl(root, "OUTPUT MODE", new List<string> { "Stereo", "Mono" }, PlayerPrefs.GetInt(PrefPrefix + "OutputMode", 0), index =>
        {
            PlayerPrefs.SetInt(PrefPrefix + "OutputMode", index);
        });
        CreateText(root, "Microphone/headphone device selection can be wired later when voice chat is added.");
    }

    void BuildPlayerPage(Transform root)
    {
        CreateToggle(root, "LEFT-HANDED MODE", PrefPrefix + "LeftHanded", false, value =>
        {
            LeftHandedMode = value;
            ApplyLeftHandedMode(value);
        });

        CreateCycleControl(root, "COLORBLIND MODE", new List<string> { "Off", "Deuteranopia", "Protanopia", "Tritanopia" }, PlayerPrefs.GetInt(PrefPrefix + "ColorblindMode", 0), index =>
        {
            ColorblindMode = index;
            PlayerPrefs.SetInt(PrefPrefix + "ColorblindMode", index);
        });
    }

    void BuildAccessibilityPage(Transform root)
    {
        CreateToggle(root, "REDUCE FLASHING", PrefPrefix + "ReduceFlashing", false, value => ReduceFlashing = value);
        CreateToggle(root, "REDUCE CAMERA SHAKE", PrefPrefix + "ReduceCameraShake", false, value => ReduceCameraShake = value);
        CreateSlider(root, "MENU SCALE", PrefPrefix + "MenuScale", 1f, 0.85f, 1.35f, value =>
        {
            if (canvas != null)
                canvas.scaleFactor = value;
        });
        CreateToggle(root, "LARGE TEXT", PrefPrefix + "LargeText", false, value => ApplyLargeText(value));
    }

    void ApplySavedSettings()
    {
        QualitySettings.vSyncCount = PlayerPrefs.GetInt(PrefPrefix + "VSync", QualitySettings.vSyncCount > 0 ? 1 : 0);
        Application.targetFrameRate = PlayerPrefs.GetInt(PrefPrefix + "FpsCap", Application.targetFrameRate);
        ApplyQualityPreset(PlayerPrefs.GetInt(PrefPrefix + "Quality", 2));
        ApplyHudScale(PlayerPrefs.GetFloat(PrefPrefix + "HudScale", 1f));
        ApplyDisplayMode(PlayerPrefs.GetInt(PrefPrefix + "DisplayMode", GetSavedDisplayModeIndex()));
        ApplyBrightness(PlayerPrefs.GetFloat(PrefPrefix + "Brightness", 1f));
        EffectScale = PlayerPrefs.GetFloat(PrefPrefix + "EffectScale", 1f);
        AudioListener.volume = PlayerPrefs.GetFloat(PrefPrefix + "MasterVolume", 1f);
        SoundEffectsVolume = PlayerPrefs.GetFloat(PrefPrefix + "SfxVolume", 1f);
        MusicVolume = PlayerPrefs.GetFloat(PrefPrefix + "MusicVolume", 1f);
        PlayerVoiceVolume = PlayerPrefs.GetFloat(PrefPrefix + "PlayerVoiceVolume", 1f);
        EnemySoundsVolume = PlayerPrefs.GetFloat(PrefPrefix + "EnemySoundsVolume", 1f);
        SubtitlesEnabled = PlayerPrefs.GetInt(PrefPrefix + "Subtitles", 1) == 1;
        LeftHandedMode = PlayerPrefs.GetInt(PrefPrefix + "LeftHanded", 0) == 1;
        ColorblindMode = PlayerPrefs.GetInt(PrefPrefix + "ColorblindMode", 0);
        ReduceFlashing = PlayerPrefs.GetInt(PrefPrefix + "ReduceFlashing", 0) == 1;
        ReduceCameraShake = PlayerPrefs.GetInt(PrefPrefix + "ReduceCameraShake", 0) == 1;
        ApplyLeftHandedMode(LeftHandedMode);
    }

    void ApplyHudScale(float value)
    {
        if (hudScalers == null)
            return;

        for (int i = 0; i < hudScalers.Length; i++)
        {
            if (hudScalers[i] != null)
                hudScalers[i].scaleFactor = value;
        }
    }

    void ApplyBrightness(float value)
    {
        if (brightnessOverlay == null)
            return;

        float darkness = Mathf.Clamp01(1f - value);
        float whiteness = Mathf.Clamp01(value - 1f);
        brightnessOverlay.color = darkness > 0f
            ? new Color(0f, 0f, 0f, darkness * 0.55f)
            : new Color(1f, 1f, 1f, whiteness * 0.18f);
    }

    void ApplyLeftHandedMode(bool value)
    {
        SwapSideRoots(rightHandToolRoot, leftHandToolRoot, value);
        SwapSideRoots(rightSideItemsRoot, leftSideItemsRoot, value);
    }

    void SwapSideRoots(Transform rightRoot, Transform leftRoot, bool useLeft)
    {
        if (rightRoot != null)
            rightRoot.gameObject.SetActive(!useLeft);

        if (leftRoot != null)
            leftRoot.gameObject.SetActive(useLeft);
    }

    void ApplyLargeText(bool enabled)
    {
        TMP_Text[] texts = canvas != null
            ? canvas.GetComponentsInChildren<TMP_Text>(true)
            : new TMP_Text[0];

        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null)
                texts[i].fontSize *= enabled ? 1.12f : 1f / 1.12f;
        }
    }

    void ApplyQualityPreset(int index)
    {
        int qualityLevel = Mathf.Clamp(index, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
        QualitySettings.SetQualityLevel(qualityLevel, true);
    }

    void ApplyDisplayMode(int index)
    {
        FullScreenMode mode = FullScreenMode.FullScreenWindow;
        if (index == 1)
            mode = FullScreenMode.MaximizedWindow;
        else if (index == 2)
            mode = FullScreenMode.Windowed;

        Screen.fullScreenMode = mode;
        PlayerPrefs.SetInt(PrefPrefix + "DisplayMode", index);
    }

    void ApplyResolution(int index)
    {
        Vector2Int resolution = CommonResolutions[Mathf.Clamp(index, 0, CommonResolutions.Length - 1)];
        Screen.SetResolution(resolution.x, resolution.y, Screen.fullScreenMode);
        PlayerPrefs.SetInt(PrefPrefix + "ResolutionIndex", index);
    }

    int GetSavedFpsIndex()
    {
        int savedCap = PlayerPrefs.GetInt(PrefPrefix + "FpsCap", Application.targetFrameRate);
        for (int i = 0; i < FpsCaps.Length; i++)
        {
            if (FpsCaps[i] == savedCap)
                return i;
        }

        return 0;
    }

    int GetSavedQualityIndex()
    {
        return Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "Quality", 2), 0, 3);
    }

    int GetSavedDisplayModeIndex()
    {
        int saved = PlayerPrefs.GetInt(PrefPrefix + "DisplayMode", -1);
        if (saved >= 0)
            return saved;

        if (Screen.fullScreenMode == FullScreenMode.Windowed)
            return 2;

        return Screen.fullScreen ? 0 : 1;
    }

    int GetSavedResolutionIndex()
    {
        return Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "ResolutionIndex", 3), 0, CommonResolutions.Length - 1);
    }

    List<string> BuildFpsOptions()
    {
        List<string> options = new List<string>();
        for (int i = 0; i < FpsCaps.Length; i++)
            options.Add(FpsCaps[i] <= 0 ? "Unlimited" : FpsCaps[i].ToString());

        return options;
    }

    List<string> BuildResolutionOptions()
    {
        List<string> options = new List<string>();
        for (int i = 0; i < CommonResolutions.Length; i++)
            options.Add($"{CommonResolutions[i].x} x {CommonResolutions[i].y}");

        return options;
    }

    void UpdateFpsCounter()
    {
        if (fpsText == null || !fpsText.gameObject.activeSelf)
            return;

        fpsFrames++;
        fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer < 0.25f)
            return;

        int fps = Mathf.RoundToInt(fpsFrames / fpsTimer);
        fpsText.text = $"FPS {fps}";
        fpsFrames = 0;
        fpsTimer = 0f;
    }

    void ShowMainPanel()
    {
        SetActive(mainPanel, true);
        SetActive(settingsPanel, false);
    }

    void ShowSettingsPanel()
    {
        SetActive(mainPanel, false);
        SetActive(settingsPanel, true);
        ShowSettingsPage(videoPage);
    }

    void ShowSettingsPage(GameObject page)
    {
        SetActive(videoPage, page == videoPage);
        SetActive(inputPage, page == inputPage);
        SetActive(audioPage, page == audioPage);
        SetActive(playerPage, page == playerPage);
        SetActive(accessibilityPage, page == accessibilityPage);
    }

    void SetActive(GameObject target, bool active)
    {
        if (target != null)
            target.SetActive(active);
    }

    GameObject CreatePanel(string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        RectTransform panel = CreateImage(
            objectName,
            parent,
            new Color(0.035f, 0.04f, 0.05f, 0.96f),
            anchorMin,
            anchorMax);

        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(26, 26, 24, 24);
        layout.spacing = 10f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return panel.gameObject;
    }

    void CreateTitle(Transform parent, string text)
    {
        TMP_Text label = CreateText(parent, text);
        label.fontSize = 42f;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }

    TMP_Text CreateText(Transform parent, string text)
    {
        GameObject textObject = new GameObject(text, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text label = textObject.GetComponent<TMP_Text>();
        label.text = text;
        label.color = new Color(0.84f, 0.95f, 0.92f, 1f);
        label.fontSize = 24f;
        label.alignment = TextAlignmentOptions.Left;
        label.enableWordWrapping = true;
        AddLayout(textObject, 48f);
        return label;
    }

    TMP_Text CreateFloatingText(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition)
    {
        GameObject textObject = new GameObject(text, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(anchorMin.x, anchorMin.y);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(180f, 40f);

        TMP_Text label = textObject.GetComponent<TMP_Text>();
        label.text = text;
        label.color = Color.white;
        label.fontSize = 24f;
        label.alignment = TextAlignmentOptions.Left;
        return label;
    }

    Button CreateButton(Transform parent, string text, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        AddLayout(buttonObject, 52f);
        SetLayoutWidth(buttonObject, 170f, 1f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.09f, 0.14f, 0.16f, 1f);

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);

        TMP_Text label = CreateText(buttonObject.transform, text);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 24f;
        label.enableWordWrapping = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = 16f;
        label.fontSizeMax = 24f;
        label.overflowMode = TextOverflowModes.Ellipsis;

        return button;
    }

    Button CreateCompactButton(Transform parent, string text, float width, float height)
    {
        GameObject buttonObject = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        buttonObject.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
        AddLayout(buttonObject, height);
        SetLayoutWidth(buttonObject, width);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.12f, 0.18f, 0.2f, 1f);

        Button button = buttonObject.GetComponent<Button>();

        TMP_Text label = CreateText(buttonObject.transform, text);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 22f;
        label.enableWordWrapping = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = 14f;
        label.fontSizeMax = 22f;
        label.overflowMode = TextOverflowModes.Ellipsis;

        return button;
    }

    Toggle CreateToggle(Transform parent, string labelText, string prefKey, bool defaultValue, System.Action<bool> onChanged)
    {
        GameObject row = CreateRow(parent, labelText);
        GameObject toggleObject = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle), typeof(Image));
        toggleObject.transform.SetParent(row.transform, false);
        toggleObject.GetComponent<Image>().color = new Color(0.12f, 0.18f, 0.2f, 1f);

        RectTransform rect = toggleObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(40f, 40f);
        AddLayout(toggleObject, 40f);
        SetLayoutWidth(toggleObject, 40f);

        Toggle toggle = toggleObject.GetComponent<Toggle>();
        Image toggleImage = toggleObject.GetComponent<Image>();
        toggle.targetGraphic = toggleImage;
        bool value = PlayerPrefs.GetInt(prefKey, defaultValue ? 1 : 0) == 1;
        toggle.isOn = value;
        SetToggleColor(toggleImage, value);
        onChanged?.Invoke(value);
        toggle.onValueChanged.AddListener(next =>
        {
            PlayerPrefs.SetInt(prefKey, next ? 1 : 0);
            SetToggleColor(toggleImage, next);
            onChanged?.Invoke(next);
        });

        return toggle;
    }

    void SetToggleColor(Image image, bool enabled)
    {
        if (image != null)
            image.color = enabled
                ? new Color(0.32f, 0.78f, 0.85f, 1f)
                : new Color(0.12f, 0.18f, 0.2f, 1f);
    }

    Slider CreateSlider(Transform parent, string labelText, string prefKey, float defaultValue, float min, float max, System.Action<float> onChanged)
    {
        GameObject row = CreateRow(parent, labelText);
        Slider slider = CreateBasicSlider(row.transform);
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = PlayerPrefs.GetFloat(prefKey, defaultValue);
        onChanged?.Invoke(slider.value);
        slider.onValueChanged.AddListener(value =>
        {
            PlayerPrefs.SetFloat(prefKey, value);
            onChanged?.Invoke(value);
        });

        return slider;
    }

    Button CreateCycleControl(Transform parent, string labelText, List<string> options, int value, System.Action<int> onChanged)
    {
        GameObject row = CreateRow(parent, labelText);
        Button button = CreateCompactButton(row.transform, string.Empty, 280f, 42f);
        TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>();
        SetLayoutWidth(button.gameObject, 320f);
        int currentIndex = Mathf.Clamp(value, 0, Mathf.Max(0, options.Count - 1));

        void ApplyIndex(int index)
        {
            currentIndex = Mathf.Clamp(index, 0, Mathf.Max(0, options.Count - 1));
            if (buttonText != null)
                buttonText.text = options.Count > 0 ? options[currentIndex] : string.Empty;

            onChanged?.Invoke(currentIndex);
        }

        button.onClick.AddListener(() =>
        {
            if (options.Count == 0)
                return;

            ApplyIndex((currentIndex + 1) % options.Count);
        });

        ApplyIndex(currentIndex);
        return button;
    }

    GameObject CreateRow(Transform parent, string labelText)
    {
        GameObject row = CreateHorizontalGroup(labelText, parent, 12f);
        AddLayout(row, 48f);

        TMP_Text label = CreateText(row.transform, labelText);
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement labelLayout = label.GetComponent<LayoutElement>();
        labelLayout.flexibleWidth = 1f;
        labelLayout.preferredHeight = 42f;

        return row;
    }

    GameObject CreateHorizontalGroup(string objectName, Transform parent, float spacing)
    {
        GameObject group = new GameObject(objectName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        group.transform.SetParent(parent, false);
        HorizontalLayoutGroup layout = group.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleCenter;
        AddLayout(group, 52f);
        return group;
    }

    GameObject CreateVerticalGroup(string objectName, Transform parent, float spacing)
    {
        GameObject group = new GameObject(objectName, typeof(RectTransform), typeof(VerticalLayoutGroup));
        group.transform.SetParent(parent, false);
        VerticalLayoutGroup layout = group.GetComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.UpperCenter;
        AddLayout(group, 52f);
        return group;
    }

    GameObject CreateSettingsPage(Transform parent, string objectName)
    {
        GameObject page = new GameObject(objectName, typeof(RectTransform), typeof(VerticalLayoutGroup));
        page.transform.SetParent(parent, false);
        VerticalLayoutGroup layout = page.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        AddLayout(page, 520f, 1f);
        return page;
    }

    Slider CreateBasicSlider(Transform parent)
    {
        GameObject sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderObject.transform.SetParent(parent, false);
        sliderObject.GetComponent<RectTransform>().sizeDelta = new Vector2(280f, 38f);
        AddLayout(sliderObject, 38f);
        SetLayoutWidth(sliderObject, 320f);

        RectTransform background = CreateImage("Background", sliderObject.transform, new Color(0.08f, 0.1f, 0.12f, 1f), Vector2.zero, Vector2.one);
        RectTransform fill = CreateImage("Fill", sliderObject.transform, new Color(0.32f, 0.78f, 0.85f, 1f), Vector2.zero, Vector2.one);
        RectTransform handle = CreateImage("Handle", sliderObject.transform, new Color(0.9f, 0.96f, 1f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        handle.sizeDelta = new Vector2(24f, 38f);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.direction = Slider.Direction.LeftToRight;
        background.SetAsFirstSibling();
        return slider;
    }

    RectTransform CreateImage(string objectName, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        imageObject.GetComponent<Image>().color = color;
        return rect;
    }

    void AddLayout(GameObject target, float preferredHeight, float flexibleHeight = 0f)
    {
        LayoutElement layout = target.GetComponent<LayoutElement>();
        if (layout == null)
            layout = target.AddComponent<LayoutElement>();

        layout.preferredHeight = preferredHeight;
        layout.flexibleHeight = flexibleHeight;
    }

    void SetLayoutWidth(GameObject target, float preferredWidth, float flexibleWidth = 0f)
    {
        LayoutElement layout = target.GetComponent<LayoutElement>();
        if (layout == null)
            layout = target.AddComponent<LayoutElement>();

        layout.preferredWidth = preferredWidth;
        layout.flexibleWidth = flexibleWidth;
    }

    void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }
}
