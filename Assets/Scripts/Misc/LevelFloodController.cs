using Unity.Netcode;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class LevelFloodController : MonoBehaviour
{
    [Header("Activation")]
    public bool startWhenLevelCompleted = true;
    public bool runOnlyOnServer = true;

    [Header("Flood Level")]
    public Transform floodVisual;
    public float startHeightOffset = -1.25f;
    public float maxHeightOffset = 5f;
    public float riseSpeed = 0.07f;
    public bool scaleRiseSpeedByRunDifficulty = true;
    public float easyRiseMultiplier = 0.75f;
    public float mediumRiseMultiplier = 1f;
    public float hardRiseMultiplier = 1.35f;
    public float gradualStartingRiseMultiplier = 0.75f;
    public float gradualEndingRiseMultiplier = 1.35f;
    [Min(1)] public int gradualRiseMaxPhase = 8;

    [Header("Visual")]
    public bool autoCreateFloodVisual = true;
    public Vector2 visualSize = new Vector2(90f, 90f);
    public Color floodColor = new Color(0.1f, 0.45f, 0.7f, 0.45f);
    public string floodVisualName = "Debug Flood Water";

    [Header("Player Death")]
    public bool killWhenWaterReachesHead = true;
    public float headHeight = 1.65f;
    public float deathDepthPadding = 0.05f;
    public float playerCheckInterval = 0.1f;

    [Header("Warning UI")]
    public bool showWarningUi = true;
    public Canvas warningCanvas;
    public GameObject warningRoot;
    public TMP_Text warningText;
    public Image warningFillImage;
    public bool autoCreateWarningUi = true;
    public string activeWarningFormat = "FLOOD {0:0}%";
    public string dangerWarningFormat = "FLOOD {0:0}% - GET BACK";
    [Range(0f, 1f)] public float dangerProgressThreshold = 0.7f;
    public Color warningFillColor = new Color(0.15f, 0.75f, 1f, 0.85f);
    public Color dangerFillColor = new Color(1f, 0.25f, 0.12f, 0.9f);

    [Header("Warning Audio")]
    public AudioSource warningAudioSource;
    public AudioClip floodStartedClip;
    public AudioClip dangerLoopClip;
    public bool playDangerLoop = true;

    [Header("Debug")]
    [SerializeField] private bool flooding;
    [SerializeField] private float currentFloodHeight;
    [SerializeField] private float startHeight;
    [SerializeField] private float maxHeight;

    private LevelObjectiveManager objectiveManager;
    private Vector3 floodCenter;
    private float playerCheckTimer;
    private bool dangerLoopPlaying;

    public bool IsFlooding => flooding;
    public float CurrentFloodHeight => currentFloodHeight;
    public float FloodProgress =>
        Mathf.InverseLerp(startHeight, maxHeight, currentFloodHeight);

    void Awake()
    {
        objectiveManager = GetComponent<LevelObjectiveManager>();
        if (objectiveManager == null)
            objectiveManager = LevelObjectiveManager.Instance;
    }

    void Start()
    {
        ResolveFloodBounds();

        if (floodVisual == null && autoCreateFloodVisual)
            floodVisual = CreateFloodVisual();

        ApplyFloodVisualPosition();
        EnsureWarningUi();
        ApplyWarningUi();
        RegisterObjectiveEvents();

        if (startWhenLevelCompleted &&
            objectiveManager != null &&
            objectiveManager.LevelCompleted)
        {
            StartFlood();
        }
    }

    void OnDisable()
    {
        UnregisterObjectiveEvents();
        StopDangerLoop();
    }

    void Update()
    {
        ApplyWarningUi();

        if (!flooding)
            return;

        if (!CanRunFlood())
            return;

        currentFloodHeight = Mathf.Min(
            maxHeight,
            currentFloodHeight + GetEffectiveRiseSpeed() * Time.deltaTime);
        ApplyFloodVisualPosition();
        ApplyWarningUi();
        UpdateWarningAudio();
        CheckPlayers();
    }

    public void StartFlood()
    {
        if (flooding)
            return;

        ResolveFloodBounds();
        flooding = true;
        currentFloodHeight = Mathf.Max(currentFloodHeight, startHeight);
        ApplyFloodVisualPosition();
        ApplyWarningUi();
        PlayFloodStartedAudio();
    }

    public void StopFlood()
    {
        flooding = false;
        ApplyWarningUi();
        StopDangerLoop();
    }

    public void ResetFlood()
    {
        ResolveFloodBounds();
        flooding = false;
        currentFloodHeight = startHeight;
        ApplyFloodVisualPosition();
        ApplyWarningUi();
        StopDangerLoop();
    }

    void RegisterObjectiveEvents()
    {
        if (!startWhenLevelCompleted || objectiveManager == null)
            return;

        objectiveManager.OnLevelCompleted -= StartFlood;
        objectiveManager.OnLevelCompleted += StartFlood;
    }

    void UnregisterObjectiveEvents()
    {
        if (objectiveManager != null)
            objectiveManager.OnLevelCompleted -= StartFlood;
    }

    bool CanRunFlood()
    {
        if (!runOnlyOnServer)
            return true;

        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null ||
            !networkManager.IsListening ||
            networkManager.IsServer;
    }

    void ResolveFloodBounds()
    {
        Bounds bounds;
        if (TryGetGeneratedMapBounds(out bounds))
        {
            floodCenter = bounds.center;
            startHeight = bounds.min.y + startHeightOffset;
            maxHeight = bounds.max.y + maxHeightOffset;
            visualSize = new Vector2(
                Mathf.Max(visualSize.x, bounds.size.x + 8f),
                Mathf.Max(visualSize.y, bounds.size.z + 8f));
        }
        else
        {
            floodCenter = transform.position;
            startHeight = transform.position.y + startHeightOffset;
            maxHeight = transform.position.y + maxHeightOffset;
        }

        if (!flooding)
            currentFloodHeight = startHeight;
    }

    bool TryGetGeneratedMapBounds(out Bounds bounds)
    {
        bounds = new Bounds(transform.position, Vector3.one);
        RoomDefinition[] rooms =
            FindObjectsByType<RoomDefinition>(FindObjectsInactive.Exclude);
        bool hasBounds = false;

        for (int i = 0; i < rooms.Length; i++)
        {
            RoomDefinition room = rooms[i];
            if (room == null)
                continue;

            Bounds roomBounds = room.GetWorldBounds();
            if (!hasBounds)
            {
                bounds = roomBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(roomBounds);
            }
        }

        return hasBounds;
    }

    Transform CreateFloodVisual()
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = floodVisualName;
        visual.transform.SetParent(transform, true);

        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
            Destroy(visualCollider);

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = CreateFloodMaterial();

        return visual.transform;
    }

    Material CreateFloodMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            return null;

        Material material = new Material(shader);
        material.name = "Debug Flood Water Material";
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", floodColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", floodColor);

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return material;
    }

    void ApplyFloodVisualPosition()
    {
        if (floodVisual == null)
            return;

        floodVisual.position = new Vector3(
            floodCenter.x,
            currentFloodHeight,
            floodCenter.z);
        floodVisual.localScale = new Vector3(
            visualSize.x,
            0.08f,
            visualSize.y);
    }

    void EnsureWarningUi()
    {
        if (!showWarningUi)
            return;

        if (warningCanvas == null && warningRoot != null)
            warningCanvas = warningRoot.GetComponentInParent<Canvas>(true);

        if (warningCanvas != null || warningRoot != null || !autoCreateWarningUi)
            return;

        GameObject canvasObject = new GameObject(
            "Flood Warning Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);

        warningCanvas = canvasObject.GetComponent<Canvas>();
        warningCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        warningCanvas.sortingOrder = 130;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        warningRoot = CreateWarningRect(
            "Flood Warning",
            canvasObject.transform,
            new Color(0.02f, 0.05f, 0.07f, 0.82f));
        RectTransform rootRect = warningRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 1f);
        rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = new Vector2(0f, -92f);
        rootRect.sizeDelta = new Vector2(360f, 72f);

        VerticalLayoutGroup layout = warningRoot.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.spacing = 8f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        warningText = CreateWarningText(warningRoot.transform);

        GameObject fillTrack = CreateWarningRect(
            "Flood Fill Track",
            warningRoot.transform,
            new Color(0f, 0f, 0f, 0.5f));
        LayoutElement trackLayout = fillTrack.AddComponent<LayoutElement>();
        trackLayout.preferredHeight = 12f;

        GameObject fillObject = CreateWarningRect(
            "Flood Fill",
            fillTrack.transform,
            warningFillColor);
        warningFillImage = fillObject.GetComponent<Image>();
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
    }

    GameObject CreateWarningRect(
        string objectName,
        Transform parent,
        Color color)
    {
        GameObject obj = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        return obj;
    }

    TMP_Text CreateWarningText(Transform parent)
    {
        GameObject textObject = new GameObject(
            "Flood Warning Text",
            typeof(RectTransform),
            typeof(TextMeshProUGUI),
            typeof(LayoutElement));
        textObject.transform.SetParent(parent, false);

        TMP_Text label = textObject.GetComponent<TMP_Text>();
        label.color = Color.white;
        label.fontSize = 22f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = 22f;

        LayoutElement layout = textObject.GetComponent<LayoutElement>();
        layout.preferredHeight = 30f;
        return label;
    }

    void ApplyWarningUi()
    {
        EnsureWarningUi();

        bool show = showWarningUi && flooding;
        if (warningRoot != null)
            warningRoot.SetActive(show);
        else if (warningCanvas != null)
            warningCanvas.gameObject.SetActive(show);

        if (!show)
            return;

        float progress = Mathf.Clamp01(FloodProgress);
        bool danger = progress >= dangerProgressThreshold;
        float percent = progress * 100f;
        string format = danger ? dangerWarningFormat : activeWarningFormat;

        if (warningText != null)
            warningText.text = string.Format(format, percent);

        if (warningFillImage != null)
        {
            warningFillImage.color = danger ? dangerFillColor : warningFillColor;
            RectTransform fillRect = warningFillImage.GetComponent<RectTransform>();
            if (fillRect != null)
            {
                fillRect.anchorMax = new Vector2(progress, 1f);
                fillRect.offsetMax = Vector2.zero;
            }
        }
    }

    void PlayFloodStartedAudio()
    {
        if (warningAudioSource == null || floodStartedClip == null)
            return;

        warningAudioSource.PlayOneShot(floodStartedClip);
    }

    void UpdateWarningAudio()
    {
        if (!playDangerLoop || warningAudioSource == null || dangerLoopClip == null)
            return;

        if (FloodProgress < dangerProgressThreshold)
        {
            StopDangerLoop();
            return;
        }

        if (dangerLoopPlaying)
            return;

        warningAudioSource.clip = dangerLoopClip;
        warningAudioSource.loop = true;
        warningAudioSource.Play();
        dangerLoopPlaying = true;
    }

    void StopDangerLoop()
    {
        if (!dangerLoopPlaying)
            return;

        if (warningAudioSource != null)
        {
            warningAudioSource.Stop();
            warningAudioSource.loop = false;
            warningAudioSource.clip = null;
        }

        dangerLoopPlaying = false;
    }

    void CheckPlayers()
    {
        if (!killWhenWaterReachesHead)
            return;

        playerCheckTimer -= Time.deltaTime;
        if (playerCheckTimer > 0f)
            return;

        playerCheckTimer = Mathf.Max(0.01f, playerCheckInterval);

        PlayerStatus[] players =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus player = players[i];
            if (player == null || player.IsDead() || player.IsTransformed())
                continue;

            float headY = player.transform.position.y + headHeight;
            if (currentFloodHeight >= headY - deathDepthPadding)
                player.RequestImmediateDeath();
        }
    }

    float GetEffectiveRiseSpeed()
    {
        return Mathf.Max(0f, riseSpeed * GetRiseMultiplierForCurrentRun());
    }

    float GetRiseMultiplierForCurrentRun()
    {
        if (!scaleRiseSpeedByRunDifficulty)
            return 1f;

        switch (RegionRunState.Difficulty)
        {
            case RunDifficulty.Easy:
                return Mathf.Max(0f, easyRiseMultiplier);
            case RunDifficulty.Medium:
                return Mathf.Max(0f, mediumRiseMultiplier);
            case RunDifficulty.Hard:
                return Mathf.Max(0f, hardRiseMultiplier);
            case RunDifficulty.Gradual:
                int phase = Mathf.Max(1, RegionRunState.PhaseNumber);
                int maxPhase = Mathf.Max(1, gradualRiseMaxPhase);
                float t = maxPhase <= 1
                    ? 1f
                    : Mathf.Clamp01((phase - 1f) / (maxPhase - 1f));
                return Mathf.Max(
                    0f,
                    Mathf.Lerp(
                        gradualStartingRiseMultiplier,
                        gradualEndingRiseMultiplier,
                        t));
            default:
                return 1f;
        }
    }
}
