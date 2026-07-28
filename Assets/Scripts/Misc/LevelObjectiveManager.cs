using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelObjectiveManager : MonoBehaviour
{
    public static LevelObjectiveManager Instance { get; private set; }

    [Header("Players")]
    public PlayerStatus[] trackedPlayers;
    public bool autoFindPlayers = true;
    public float roomDiscoveryInterval = 0.25f;
    public float roomBoundsPadding = 0.5f;

    [Header("Objectives")]
    [Range(0f, 1f)] public float requiredCleanPercent = 0.8f;
    public bool requireFinalRoomDiscovered = true;
    public bool requireAllWaterSourcesClean = false;
    public bool completeOnlyOnce = true;

    [Header("HUD")]
    public TMP_Text objectiveText;
    public TMP_Text progressText;
    public TMP_Text cleaningProgressText;
    public Slider cleaningProgressBar;
    public Image cleaningProgressFill;
    public TMP_Text poolCounterText;
    public TMP_Text levelInfoText;
    public bool autoBindNewHudFields = true;
    public string activeObjectiveLabel = "Clean the pools and reach the exit";
    public string completedObjectiveLabel = "Objectives complete";
    public string cleaningProgressFormat = "Cleaning: {0}%";
    public string poolCounterFormat = "Pools {0}/{1}";
    public string levelInfoFormat = "Level {0}";
    public string regionLevelInfoFormat = "Level {0} - {1}";

    [Header("Level Info")]
    [Min(1)] public int levelNumber = 1;
    [Min(0)] public int requiredPoolCountOverride;
    public bool countGeneratedPoolRooms = true;

    [Header("Debug")]
    [SerializeField] private int discoveredRoomCount;
    [SerializeField] private bool finalRoomDiscovered;
    [SerializeField] private float currentCleanPercent;
    [SerializeField] private int registeredDirtSpotCount;
    [SerializeField] private int cleanedDirtSpotCount;
    [SerializeField] private int requiredPoolCount;
    [SerializeField] private int cleanedPoolCount;
    [SerializeField] private bool levelCompleted;

    public event Action<RoomDefinition, int> OnRoomDiscovered;
    public event Action OnObjectiveStateChanged;
    public event Action OnLevelCompleted;

    private readonly List<RoomDefinition> discoveredRooms = new List<RoomDefinition>();
    private readonly HashSet<RoomDefinition> discoveredRoomSet = new HashSet<RoomDefinition>();
    private readonly HashSet<DirtSpot> registeredDirtSpots = new HashSet<DirtSpot>();
    private readonly HashSet<DirtSpot> cleanedDirtSpots = new HashSet<DirtSpot>();
    private float discoveryTimer;
    private float objectiveTimer;

    public int DiscoveredRoomCount => discoveredRoomCount;
    public bool FinalRoomDiscovered => finalRoomDiscovered;
    public float CurrentCleanPercent => currentCleanPercent;
    public int RequiredPoolCount => requiredPoolCount;
    public int CleanedPoolCount => cleanedPoolCount;
    public bool LevelCompleted => levelCompleted;
    public IReadOnlyList<RoomDefinition> DiscoveredRooms => discoveredRooms;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        UnregisterDirtSpotEvents();

        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        AutoBindHudFields();
        RefreshObjectiveState();
        UpdateObjectiveHUD();
    }

    void Update()
    {
        discoveryTimer -= Time.deltaTime;
        objectiveTimer -= Time.deltaTime;

        if (discoveryTimer <= 0f)
        {
            discoveryTimer = Mathf.Max(0.05f, roomDiscoveryInterval);
            UpdateRoomDiscovery();
        }

        if (objectiveTimer <= 0f)
        {
            objectiveTimer = 0.5f;
            RefreshObjectiveState();
        }
    }

    public void RegisterRoomDiscovered(RoomDefinition room)
    {
        if (room == null || discoveredRoomSet.Contains(room))
            return;

        discoveredRoomSet.Add(room);
        discoveredRooms.Add(room);
        discoveredRoomCount = discoveredRooms.Count;

        if (room.category == RoomCategory.Final)
            finalRoomDiscovered = true;

        OnRoomDiscovered?.Invoke(room, discoveredRooms.Count - 1);
        RefreshObjectiveState();
    }

    public bool IsRoomDiscovered(RoomDefinition room)
    {
        return room != null && discoveredRoomSet.Contains(room);
    }

    public void RefreshObjectiveState()
    {
        currentCleanPercent = CalculateCleanPercent();
        RefreshPoolProgress();
        bool waterSourcesReady = !requireAllWaterSourcesClean || AreAllWaterSourcesClean();
        bool finalReady = !requireFinalRoomDiscovered || finalRoomDiscovered;
        bool cleanReady = currentCleanPercent >= requiredCleanPercent;
        bool completedNow = cleanReady && finalReady && waterSourcesReady;

        if (completedNow && (!levelCompleted || !completeOnlyOnce))
        {
            levelCompleted = true;
            OnLevelCompleted?.Invoke();
        }

        UpdateObjectiveHUD();
        OnObjectiveStateChanged?.Invoke();
    }

    void UpdateRoomDiscovery()
    {
        if (autoFindPlayers)
            trackedPlayers = FindObjectsOfType<PlayerStatus>();

        if (trackedPlayers == null || trackedPlayers.Length == 0)
            return;

        RoomDefinition[] rooms = FindObjectsOfType<RoomDefinition>();
        for (int r = 0; r < rooms.Length; r++)
        {
            RoomDefinition room = rooms[r];
            if (room == null || discoveredRoomSet.Contains(room))
                continue;

            Bounds bounds = room.GetWorldBounds();
            bounds.Expand(roomBoundsPadding);

            for (int p = 0; p < trackedPlayers.Length; p++)
            {
                PlayerStatus player = trackedPlayers[p];
                if (player == null || player.IsDead()) continue;

                if (bounds.Contains(player.transform.position))
                {
                    RegisterRoomDiscovered(room);
                    break;
                }
            }
        }
    }

    float CalculateCleanPercent()
    {
        RegisterKnownDirtSpots();

        if (registeredDirtSpots.Count == 0)
        {
            UpdateDirtDebugCounts();
            return 1f;
        }

        float cleanedAmount = 0f;

        foreach (DirtSpot dirt in registeredDirtSpots)
        {
            if (dirt == null || dirt.IsCleaned || cleanedDirtSpots.Contains(dirt))
            {
                cleanedAmount += 1f;
                continue;
            }

            cleanedAmount += Mathf.Clamp01(1f - dirt.GetDirtPercent());
        }

        UpdateDirtDebugCounts();
        return Mathf.Clamp01(cleanedAmount / registeredDirtSpots.Count);
    }

    void RegisterKnownDirtSpots()
    {
        DirtSpot[] dirtSpots = FindObjectsOfType<DirtSpot>();
        for (int i = 0; i < dirtSpots.Length; i++)
            RegisterDirtSpot(dirtSpots[i]);
    }

    void RegisterDirtSpot(DirtSpot dirt)
    {
        if (dirt == null || registeredDirtSpots.Contains(dirt))
            return;

        registeredDirtSpots.Add(dirt);
        dirt.OnCleaned += HandleDirtSpotCleaned;

        if (dirt.IsCleaned)
            HandleDirtSpotCleaned(dirt);
    }

    void HandleDirtSpotCleaned(DirtSpot dirt)
    {
        if (dirt == null)
            return;

        cleanedDirtSpots.Add(dirt);
        RefreshObjectiveState();
    }

    void UpdateDirtDebugCounts()
    {
        registeredDirtSpotCount = registeredDirtSpots.Count;
        cleanedDirtSpotCount = cleanedDirtSpots.Count;
    }

    void UnregisterDirtSpotEvents()
    {
        foreach (DirtSpot dirt in registeredDirtSpots)
        {
            if (dirt != null)
                dirt.OnCleaned -= HandleDirtSpotCleaned;
        }
    }

    bool AreAllWaterSourcesClean()
    {
        WaterSourceDryable[] sources = FindObjectsOfType<WaterSourceDryable>();
        for (int i = 0; i < sources.Length; i++)
        {
            WaterSourceDryable source = sources[i];
            if (source == null || source.isDry) continue;
            if (source.waterQuality == WaterQuality.Contaminated)
                return false;
        }

        return true;
    }

    void UpdateObjectiveHUD()
    {
        AutoBindHudFields();

        if (objectiveText != null)
            objectiveText.text = levelCompleted ? completedObjectiveLabel : activeObjectiveLabel;

        int cleanPercent = Mathf.RoundToInt(currentCleanPercent * 100f);

        if (cleaningProgressText != null)
            cleaningProgressText.text = string.Format(cleaningProgressFormat, cleanPercent);

        if (cleaningProgressBar != null)
            cleaningProgressBar.value = currentCleanPercent;

        if (cleaningProgressFill != null)
            cleaningProgressFill.fillAmount = currentCleanPercent;

        if (poolCounterText != null)
            poolCounterText.text = string.Format(
                poolCounterFormat,
                cleanedPoolCount,
                requiredPoolCount);

        if (levelInfoText != null)
            levelInfoText.text = GetLevelInfoText();

        if (progressText == null) return;

        int requiredPercent = Mathf.RoundToInt(requiredCleanPercent * 100f);
        string finalText = requireFinalRoomDiscovered
            ? finalRoomDiscovered ? "Exit found" : "Find exit"
            : "Exit optional";

        progressText.text = $"Clean {cleanPercent}% / {requiredPercent}% - Rooms {discoveredRoomCount} - {finalText}";
    }

    void RefreshPoolProgress()
    {
        requiredPoolCount = Mathf.Max(0, requiredPoolCountOverride);

        if (requiredPoolCount <= 0 && countGeneratedPoolRooms)
            requiredPoolCount = CountGeneratedPoolRooms();

        if (requiredPoolCount <= 0)
            requiredPoolCount = 1;

        cleanedPoolCount = Mathf.Clamp(
            Mathf.FloorToInt(currentCleanPercent * requiredPoolCount + 0.001f),
            0,
            requiredPoolCount);

        if (levelCompleted)
            cleanedPoolCount = requiredPoolCount;
    }

    int CountGeneratedPoolRooms()
    {
        int count = 0;
        RoomDefinition[] rooms = FindObjectsOfType<RoomDefinition>();
        for (int i = 0; i < rooms.Length; i++)
        {
            if (rooms[i] != null && rooms[i].category == RoomCategory.Pool)
                count++;
        }

        return count;
    }

    string GetLevelInfoText()
    {
        string regionName = RegionRunState.HasSelectedRegion
            ? RegionRunState.RegionName
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(regionName))
            return string.Format(regionLevelInfoFormat, levelNumber, regionName);

        return string.Format(levelInfoFormat, levelNumber);
    }

    void AutoBindHudFields()
    {
        if (!autoBindNewHudFields)
            return;

        if (cleaningProgressText != null &&
            cleaningProgressBar != null &&
            poolCounterText != null &&
            levelInfoText != null)
        {
            return;
        }

        if (cleaningProgressBar == null)
        {
            Slider[] sliders = FindObjectsOfType<Slider>(true);
            for (int i = 0; i < sliders.Length; i++)
            {
                Slider slider = sliders[i];
                if (slider != null && slider.gameObject.name.Contains("Progress Bar"))
                {
                    cleaningProgressBar = slider;
                    break;
                }
            }
        }

        TMP_Text[] texts = FindObjectsOfType<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null) continue;

            string objectName = text.gameObject.name;
            string parentName = text.transform.parent != null
                ? text.transform.parent.name
                : string.Empty;
            string textValue = text.text;

            if (cleaningProgressText == null &&
                (objectName.Contains("Cleaning") ||
                 parentName.Contains("Progress Bar") ||
                 textValue.Contains("Cleaning")))
            {
                cleaningProgressText = text;
            }
            else if (poolCounterText == null &&
                (objectName.Contains("Pools") ||
                 parentName.Contains("Pools") ||
                 textValue.Contains("Pools")))
            {
                poolCounterText = text;
            }
            else if (levelInfoText == null &&
                (objectName.Contains("Level") ||
                 parentName.Contains("Level") ||
                 textValue.Contains("Level Info")))
            {
                levelInfoText = text;
            }
        }
    }
}
