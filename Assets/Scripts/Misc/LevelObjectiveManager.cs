using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

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
    public string activeObjectiveLabel = "Clean the pools and reach the exit";
    public string completedObjectiveLabel = "Objectives complete";

    [Header("Debug")]
    [SerializeField] private int discoveredRoomCount;
    [SerializeField] private bool finalRoomDiscovered;
    [SerializeField] private float currentCleanPercent;
    [SerializeField] private bool levelCompleted;

    public event Action<RoomDefinition, int> OnRoomDiscovered;
    public event Action OnObjectiveStateChanged;
    public event Action OnLevelCompleted;

    private readonly List<RoomDefinition> discoveredRooms = new List<RoomDefinition>();
    private readonly HashSet<RoomDefinition> discoveredRoomSet = new HashSet<RoomDefinition>();
    private float discoveryTimer;
    private float objectiveTimer;

    public int DiscoveredRoomCount => discoveredRoomCount;
    public bool FinalRoomDiscovered => finalRoomDiscovered;
    public float CurrentCleanPercent => currentCleanPercent;
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
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
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
        DirtSpot[] dirtSpots = FindObjectsOfType<DirtSpot>();
        if (dirtSpots == null || dirtSpots.Length == 0)
            return 1f;

        float dirtyPercentTotal = 0f;
        int counted = 0;

        for (int i = 0; i < dirtSpots.Length; i++)
        {
            DirtSpot dirt = dirtSpots[i];
            if (dirt == null || !dirt.gameObject.activeInHierarchy) continue;

            dirtyPercentTotal += Mathf.Clamp01(dirt.GetDirtPercent());
            counted++;
        }

        if (counted == 0)
            return 1f;

        return 1f - Mathf.Clamp01(dirtyPercentTotal / counted);
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
        if (objectiveText != null)
            objectiveText.text = levelCompleted ? completedObjectiveLabel : activeObjectiveLabel;

        if (progressText == null) return;

        int cleanPercent = Mathf.RoundToInt(currentCleanPercent * 100f);
        int requiredPercent = Mathf.RoundToInt(requiredCleanPercent * 100f);
        string finalText = requireFinalRoomDiscovered
            ? finalRoomDiscovered ? "Exit found" : "Find exit"
            : "Exit optional";

        progressText.text = $"Clean {cleanPercent}% / {requiredPercent}% - Rooms {discoveredRoomCount} - {finalText}";
    }
}
