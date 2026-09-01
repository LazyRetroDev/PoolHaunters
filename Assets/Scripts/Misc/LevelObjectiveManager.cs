using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LevelObjectiveManager : MonoBehaviour
{
    const string PoolObjectiveStateMessageName = "PoolObjectiveState";
    const string PoolObjectiveStateRequestMessageName = "PoolObjectiveStateRequest";
    const string PoolDirtCleanMessageName = "PoolDirtClean";
    const string PoolDirtCleanRequestMessageName = "PoolDirtCleanRequest";
    const string PoolMandatoryStateMessageName = "PoolMandatoryState";

    public static LevelObjectiveManager Instance { get; private set; }

    [Header("Players")]
    public PlayerStatus[] trackedPlayers;
    public bool autoFindPlayers = true;
    public float roomDiscoveryInterval = 0.25f;
    public float roomBoundsPadding = 0.5f;

    [Header("Objectives")]
    [Range(0f, 1f)] public float requiredCleanPercent = 0.8f;
    public bool requireTotalCleanPercent = false;
    public bool requireWaterValveObjective = true;
    public bool requireFinalRoomDiscovered = true;
    public bool requireAllWaterSourcesClean = false;
    public bool requireAllRequiredPoolsClean = true;
    public bool completeOnlyOnce = true;

    [Header("Flooding")]
    public bool ensureFloodController = true;

    [Header("Random Mandatory Pools")]
    public bool randomizeMandatoryPools = false;
    [HideInInspector]
    [Min(1)] public int minMandatoryPools = 1;
    [HideInInspector]
    [Min(1)] public int maxMandatoryPools = 3;
    [Min(1)] public int guaranteedMandatoryPools = 1;
    [Min(0)] public int guaranteedOptionalPools = 1;
    [Range(0f, 1f)] public float extraPoolMandatoryChance = 0.5f;

    [Header("Phase Profile")]
    public bool applyPhaseProfileOnStart = true;
    public RunPhaseProfile fallbackPhaseProfile;
    public RunPhaseProfile[] phaseProfiles = new RunPhaseProfile[0];

    [Header("HUD")]
    public TMP_Text objectiveText;
    public TMP_Text progressText;
    public TMP_Text cleaningProgressText;
    public Slider cleaningProgressBar;
    public Image cleaningProgressFill;
    public TMP_Text currentPoolProgressText;
    public Slider currentPoolProgressBar;
    public Image currentPoolProgressFill;
    public TMP_Text poolCounterText;
    public TMP_Text levelInfoText;
    public Slider cleanGoalSlider;
    public TMP_Text cleanGoalText;
    public Slider poolCleanGoalSlider;
    public TMP_Text poolCleanGoalText;
    public bool autoBindNewHudFields = true;
    public bool autoFindCleanGoalUI = true;
    public bool autoFindPoolCleanGoalUI = true;
    public bool showCleanGoalOnlyAfterWaterValve = true;
    public string cleanGoalObjectName = "CleanGoal";
    public string cleanGoalTextObjectName = "goaltext";
    public string poolCleanGoalObjectName = "PoolCleanGoal";
    public string poolCleanGoalTextObjectName = "PGoalText";
    public float poolCleanGoalHideDelay = 2.0f;
    public string findWaterValveObjectiveLabel = "Find the water valve";
    public string findWaterValveProgressLabel = "Turn the water valve to start cleaning";
    public string activeObjectiveLabel = "Clean the required pools";
    public string completedObjectiveLabel = "Objectives complete";
    public string returnToSubmarineObjectiveLabel = "Return to the Submarine Room";
    public string cleaningProgressFormat = "Total Cleaning: {0}%";
    public string currentPoolProgressFormat = "Pool Cleaning: {0}%";
    public string poolCounterFormat = "Pools {0}/{1}";
    public string levelInfoFormat = "Level {0}";
    public string regionLevelInfoFormat = "Phase {0} - {1}";
    public string fungalPoolObjectiveFormat = "Remove pool fungi: {0} left";
    public string electricPoolObjectivePowered = "Disable the electric pool device";
    public string electricPoolObjectiveSafeFormat = "Electric pool safe: {0}s until power returns";

    [Header("Level Info")]
    [Min(1)] public int levelNumber = 1;

    [Header("Debug")]
    [SerializeField] private int discoveredRoomCount;
    [SerializeField] private bool finalRoomDiscovered;
    [SerializeField] private bool waterValveActivated;
    [SerializeField] private float currentCleanPercent;
    [SerializeField] private int registeredDirtSpotCount;
    [SerializeField] private int cleanedDirtSpotCount;
    [SerializeField] private int requiredPoolCount;
    [SerializeField] private int cleanedRequiredPoolCount;
    [SerializeField] private float currentPoolCleanPercent;
    [SerializeField] private bool levelCompleted;

    public event Action<RoomDefinition, int> OnRoomDiscovered;
    public event Action OnWaterValveActivated;
    public event Action OnObjectiveStateChanged;
    public event Action OnLevelCompleted;

    private readonly List<RoomDefinition> discoveredRooms = new List<RoomDefinition>();
    private readonly HashSet<RoomDefinition> discoveredRoomSet = new HashSet<RoomDefinition>();
    private readonly HashSet<DirtSpot> registeredDirtSpots = new HashSet<DirtSpot>();
    private readonly HashSet<DirtSpot> cleanedDirtSpots = new HashSet<DirtSpot>();
    private readonly HashSet<SwimmingPoolObjective> registeredPools =
        new HashSet<SwimmingPoolObjective>();
    private readonly HashSet<SwimmingPoolObjective> pendingOutgoingPoolStates =
        new HashSet<SwimmingPoolObjective>();
    private readonly Dictionary<int, byte> pendingPoolNetworkStates =
        new Dictionary<int, byte>();
    private readonly Dictionary<int, bool> pendingPoolMandatoryStates =
        new Dictionary<int, bool>();
    private float discoveryTimer;
    private float objectiveTimer;
    private NetworkManager poolSyncNetworkManager;
    private Coroutine poolSyncRegistrationCoroutine;
    private bool poolMessageHandlersRegistered;

    public int DiscoveredRoomCount => discoveredRoomCount;
    public bool FinalRoomDiscovered => finalRoomDiscovered;
    public bool WaterValveActivated => !requireWaterValveObjective || waterValveActivated;
    public float CurrentCleanPercent => currentCleanPercent;
    public int RequiredPoolCount => requiredPoolCount;
    public int CleanedRequiredPoolCount => cleanedRequiredPoolCount;
    public float CurrentPoolCleanPercent => currentPoolCleanPercent;
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

        if (GetComponent<LevelRewardTracker>() == null)
            gameObject.AddComponent<LevelRewardTracker>();

        if (ensureFloodController && GetComponent<LevelFloodController>() == null)
            gameObject.AddComponent<LevelFloodController>();
    }

    void OnEnable()
    {
        GameLocalization.LanguageChanged += HandleLanguageChanged;
    }

    void OnDisable()
    {
        GameLocalization.LanguageChanged -= HandleLanguageChanged;
    }

    void OnDestroy()
    {
        UnregisterDirtSpotEvents();
        UnregisterPoolEvents();
        UnregisterPoolSyncMessaging();

        if (Instance == this)
            Instance = null;
    }

    void HandleLanguageChanged(GameLanguage language)
    {
        UpdateObjectiveHUD();
    }

    void OnValidate()
    {
        minMandatoryPools = Mathf.Max(1, minMandatoryPools);
        maxMandatoryPools = Mathf.Max(minMandatoryPools, maxMandatoryPools);
        guaranteedMandatoryPools = Mathf.Max(1, guaranteedMandatoryPools);
        guaranteedOptionalPools = Mathf.Max(0, guaranteedOptionalPools);
        extraPoolMandatoryChance = Mathf.Clamp01(extraPoolMandatoryChance);
    }

    public void ApplyPhaseProfile(RunPhaseProfile profile)
    {
        if (profile == null)
            return;

        randomizeMandatoryPools = profile.randomizeMandatoryPools;
        guaranteedMandatoryPools = profile.guaranteedMandatoryPools;
        guaranteedOptionalPools = profile.guaranteedOptionalPools;
        extraPoolMandatoryChance = profile.extraPoolMandatoryChance;
    }

    RunPhaseProfile ResolvePhaseProfile()
    {
        RunPhaseProfile profile = GetPhaseProfileForCurrentRun();
        if (profile != null)
            return profile;

        return fallbackPhaseProfile;
    }

    RunPhaseProfile GetPhaseProfileForCurrentRun()
    {
        if (phaseProfiles == null || phaseProfiles.Length == 0)
            return null;

        int phaseIndex = RegionRunState.HasSelectedRegion
            ? RegionRunState.PhaseNumber - 1
            : 0;
        phaseIndex = Mathf.Clamp(phaseIndex, 0, phaseProfiles.Length - 1);

        return phaseProfiles[phaseIndex];
    }

    void Start()
    {
        AutoBindHudFields();
        BindCleanGoalUI();
        if (applyPhaseProfileOnStart)
            ApplyPhaseProfile(ResolvePhaseProfile());
        StartPoolSyncRegistration();
        RefreshObjectiveState();
        UpdateObjectiveHUD();

        if (randomizeMandatoryPools)
        {
            StartCoroutine(InitializeMandatoryPoolsRoutine());
        }
    }

    IEnumerator InitializeMandatoryPoolsRoutine()
    {
        // Wait until at least one pool is spawned by the level generator
        while (FindObjectsByType<SwimmingPoolObjective>(FindObjectsInactive.Include).Length == 0)
        {
            yield return new WaitForSeconds(0.25f);
        }

        // Wait a bit more to ensure all rooms/pools have finished spawning
        yield return new WaitForSeconds(1.0f);

        // ONLY SERVER SHOULD DO THIS
        bool isServer = NetworkManager.Singleton == null || 
                        !NetworkManager.Singleton.IsListening || 
                        NetworkManager.Singleton.IsServer;

        if (isServer)
        {
            InitializeMandatoryPools();
        }

        RefreshObjectiveState();
        UpdateObjectiveHUD();
    }

    void InitializeMandatoryPools()
    {
        if (!randomizeMandatoryPools)
            return;

        SwimmingPoolObjective[] allPools = FindObjectsByType<SwimmingPoolObjective>(FindObjectsInactive.Include);
        if (allPools == null || allPools.Length == 0) return;

        // Shuffle allPools array
        for (int i = 0; i < allPools.Length; i++)
        {
            SwimmingPoolObjective temp = allPools[i];
            int randomIndex = UnityEngine.Random.Range(i, allPools.Length);
            allPools[i] = allPools[randomIndex];
            allPools[randomIndex] = temp;
        }

        for (int i = 0; i < allPools.Length; i++)
        {
            if (allPools[i] != null)
            {
                allPools[i].RequiredForLevelCompletion =
                    IsPoolMandatoryByRule(i, allPools.Length);
                SendPoolMandatoryStateToConnectedClients(allPools[i]);
            }
        }

        UpdatePoolDebugCounts();
    }

    bool IsPoolMandatoryByRule(int shuffledPoolIndex, int totalPoolCount)
    {
        if (totalPoolCount <= 1)
            return true;

        int mandatoryCount = Mathf.Clamp(
            guaranteedMandatoryPools,
            1,
            totalPoolCount);
        int optionalCount = totalPoolCount > mandatoryCount
            ? Mathf.Clamp(
                guaranteedOptionalPools,
                0,
                totalPoolCount - mandatoryCount)
            : 0;

        if (shuffledPoolIndex < mandatoryCount)
            return true;

        if (shuffledPoolIndex < mandatoryCount + optionalCount)
            return false;

        return UnityEngine.Random.value <= extraPoolMandatoryChance;
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

        UpdatePoolCleanGoalUI();
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

    public void ActivateWaterValve()
    {
        if (waterValveActivated)
            return;

        waterValveActivated = true;
        OnWaterValveActivated?.Invoke();
        RefreshObjectiveState();
    }

    public void RegisterPoolObjective(SwimmingPoolObjective pool)
    {
        if (pool == null || registeredPools.Contains(pool))
            return;

        registeredPools.Add(pool);
        pool.OnPoolCleaned += HandlePoolCleaned;
        pool.OnPoolStateChanged += HandlePoolStateChanged;
        ApplyPendingPoolNetworkState(pool);
        ApplyPendingPoolMandatoryState(pool);
        UpdatePoolDebugCounts();
    }

    public void UnregisterPoolObjective(SwimmingPoolObjective pool)
    {
        if (pool == null || !registeredPools.Remove(pool))
            return;

        pool.OnPoolCleaned -= HandlePoolCleaned;
        pool.OnPoolStateChanged -= HandlePoolStateChanged;
        pendingOutgoingPoolStates.Remove(pool);
        UpdatePoolDebugCounts();
    }

    public void NotifyPoolObjectiveStateChanged(SwimmingPoolObjective pool)
    {
        if (pool == null)
            return;

        RegisterPoolObjective(pool);
        RefreshObjectiveState();
    }

    public void NotifyPoolDirtSpotCleaned(
        SwimmingPoolObjective pool,
        DirtSpot dirtSpot,
        Vector3 worldPoint,
        float worldRadius,
        float amount)
    {
        if (pool == null ||
            dirtSpot == null ||
            pool.IsApplyingSynchronizedState)
        {
            return;
        }

        RegisterPoolObjective(pool);

        int dirtSpotIndex;
        if (!pool.TryGetDirtSpotIndex(dirtSpot, out dirtSpotIndex))
            return;

        SyncPoolDirtClean(
            pool,
            dirtSpotIndex,
            worldPoint,
            worldRadius,
            amount);
    }

    public void RefreshObjectiveState()
    {
        bool valveReady = WaterValveActivated;
        currentCleanPercent = valveReady ? CalculateCleanPercent() : 0f;
        bool waterSourcesReady = !requireAllWaterSourcesClean || AreAllWaterSourcesClean();
        bool poolsReady = !requireAllRequiredPoolsClean || AreAllRequiredPoolsClean();
        bool finalReady = !requireFinalRoomDiscovered || finalRoomDiscovered;
        bool cleanReady =
            !requireTotalCleanPercent ||
            currentCleanPercent >= requiredCleanPercent;
        bool completedNow =
            valveReady &&
            cleanReady &&
            finalReady &&
            waterSourcesReady &&
            poolsReady;

        if (completedNow && (!levelCompleted || !completeOnlyOnce))
        {
            levelCompleted = true;
            OnLevelCompleted?.Invoke();
        }

        UpdateObjectiveHUD();
        OnObjectiveStateChanged?.Invoke();
    }

    public void DebugCleanAllDirt()
    {
        DirtSpot[] dirtSpots =
            FindObjectsByType<DirtSpot>(FindObjectsInactive.Include);
        for (int i = 0; i < dirtSpots.Length; i++)
        {
            if (dirtSpots[i] != null)
                dirtSpots[i].ForceClean();
        }

        RefreshObjectiveState();
    }

    public void DebugCleanAllPools()
    {
        RegisterKnownPoolObjectives();

        foreach (SwimmingPoolObjective pool in registeredPools)
        {
            if (pool != null && pool.RequiredForLevelCompletion)
                pool.ForceClean();
        }

        RefreshObjectiveState();
    }

    public void DebugCompleteObjectives()
    {
        waterValveActivated = true;
        finalRoomDiscovered = true;
        DebugCleanAllDirt();
        DebugCleanAllPools();

        if (!levelCompleted)
        {
            levelCompleted = true;
            UpdateObjectiveHUD();
            OnObjectiveStateChanged?.Invoke();
            OnLevelCompleted?.Invoke();
        }
    }

    public void DebugRevealAllRooms()
    {
        RoomDefinition[] rooms =
            FindObjectsByType<RoomDefinition>(FindObjectsInactive.Include);
        for (int i = 0; i < rooms.Length; i++)
            RegisterRoomDiscovered(rooms[i]);

        RefreshObjectiveState();
    }

    void UpdateRoomDiscovery()
    {
        if (autoFindPlayers)
            trackedPlayers =
                FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);

        if (trackedPlayers == null || trackedPlayers.Length == 0)
            return;

        RoomDefinition[] rooms =
            FindObjectsByType<RoomDefinition>(FindObjectsInactive.Exclude);
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
        DirtSpot[] dirtSpots =
            FindObjectsByType<DirtSpot>(FindObjectsInactive.Exclude);
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

    void HandlePoolCleaned(SwimmingPoolObjective pool)
    {
        SyncCleanedPoolState(pool);
        RefreshObjectiveState();
    }

    void HandlePoolStateChanged(SwimmingPoolObjective pool)
    {
        UpdatePoolDebugCounts();
    }

    void UnregisterDirtSpotEvents()
    {
        foreach (DirtSpot dirt in registeredDirtSpots)
        {
            if (dirt != null)
                dirt.OnCleaned -= HandleDirtSpotCleaned;
        }
    }

    void UnregisterPoolEvents()
    {
        foreach (SwimmingPoolObjective pool in registeredPools)
        {
            if (pool == null)
                continue;

            pool.OnPoolCleaned -= HandlePoolCleaned;
            pool.OnPoolStateChanged -= HandlePoolStateChanged;
        }
    }

    bool AreAllWaterSourcesClean()
    {
        WaterSourceDryable[] sources =
            FindObjectsByType<WaterSourceDryable>(FindObjectsInactive.Exclude);
        for (int i = 0; i < sources.Length; i++)
        {
            WaterSourceDryable source = sources[i];
            if (source == null || source.isDry) continue;
            if (source.waterQuality == WaterQuality.Contaminated)
                return false;
        }

        return true;
    }

    bool AreAllRequiredPoolsClean()
    {
        RegisterKnownPoolObjectives();
        UpdatePoolDebugCounts();

        if (requiredPoolCount == 0)
            return true;

        foreach (SwimmingPoolObjective pool in registeredPools)
        {
            if (pool == null || !pool.RequiredForLevelCompletion)
                continue;

            pool.RefreshAndEvaluateCleanState();
            if (!pool.IsCleaned)
                return false;
        }

        return true;
    }

    void RegisterKnownPoolObjectives()
    {
        SwimmingPoolObjective[] pools =
            FindObjectsByType<SwimmingPoolObjective>(FindObjectsInactive.Include);
        for (int i = 0; i < pools.Length; i++)
            RegisterPoolObjective(pools[i]);
    }

    void UpdatePoolDebugCounts()
    {
        requiredPoolCount = 0;
        cleanedRequiredPoolCount = 0;

        foreach (SwimmingPoolObjective pool in registeredPools)
        {
            if (pool == null || !pool.RequiredForLevelCompletion)
                continue;

            requiredPoolCount++;
            if (pool.IsCleaned)
                cleanedRequiredPoolCount++;
        }
    }

    float CalculateRequiredPoolCleanPercent()
    {
        RegisterKnownPoolObjectives();

        float cleanAmount = 0f;
        int requiredCount = 0;

        foreach (SwimmingPoolObjective pool in registeredPools)
        {
            if (pool == null || !pool.RequiredForLevelCompletion)
                continue;

            pool.RefreshAndEvaluateCleanState();
            requiredCount++;
            cleanAmount += pool.IsCleaned ? 1f : Mathf.Clamp01(pool.CleanProgress);
        }

        UpdatePoolDebugCounts();
        return requiredCount > 0 ? Mathf.Clamp01(cleanAmount / requiredCount) : 0f;
    }

    float CalculateCurrentPoolCleanPercent()
    {
        SwimmingPoolObjective pool = FindFocusedRequiredPool();
        currentPoolCleanPercent = pool != null
            ? pool.IsCleaned ? 1f : Mathf.Clamp01(pool.CleanProgress)
            : requiredPoolCount > 0 && cleanedRequiredPoolCount >= requiredPoolCount
                ? 1f
                : 0f;
        return currentPoolCleanPercent;
    }

    SwimmingPoolObjective FindFocusedRequiredPool()
    {
        RegisterKnownPoolObjectives();

        if (autoFindPlayers)
            trackedPlayers =
                FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);

        SwimmingPoolObjective closestPool = null;
        float closestDistanceSqr = float.MaxValue;

        if (trackedPlayers != null && trackedPlayers.Length > 0)
        {
            foreach (SwimmingPoolObjective pool in registeredPools)
            {
                if (!CanUseFocusedPool(pool))
                    continue;

                Vector3 poolPosition = pool.transform.position;
                for (int i = 0; i < trackedPlayers.Length; i++)
                {
                    PlayerStatus player = trackedPlayers[i];
                    if (player == null || player.IsDead())
                        continue;

                    float distanceSqr =
                        (player.transform.position - poolPosition).sqrMagnitude;
                    if (distanceSqr < closestDistanceSqr)
                    {
                        closestDistanceSqr = distanceSqr;
                        closestPool = pool;
                    }
                }
            }
        }

        if (closestPool != null)
            return closestPool;

        SwimmingPoolObjective fallbackPool = null;
        float lowestProgress = float.MaxValue;

        foreach (SwimmingPoolObjective pool in registeredPools)
        {
            if (!CanUseFocusedPool(pool))
                continue;

            float progress = pool.IsCleaned ? 1f : Mathf.Clamp01(pool.CleanProgress);
            if (progress < lowestProgress)
            {
                lowestProgress = progress;
                fallbackPool = pool;
            }
        }

        return fallbackPool;
    }

    bool CanUseFocusedPool(SwimmingPoolObjective pool)
    {
        return pool != null &&
            pool.RequiredForLevelCompletion &&
            !pool.IsCleaned;
    }

    void StartPoolSyncRegistration()
    {
        if (poolMessageHandlersRegistered || poolSyncRegistrationCoroutine != null)
            return;

        poolSyncRegistrationCoroutine = StartCoroutine(
            RegisterPoolSyncMessagingWhenReady());
    }

    IEnumerator RegisterPoolSyncMessagingWhenReady()
    {
        while (isActiveAndEnabled)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null &&
                networkManager.IsListening &&
                networkManager.CustomMessagingManager != null)
            {
                RegisterPoolSyncMessaging(networkManager);
                poolSyncRegistrationCoroutine = null;
                yield break;
            }

            yield return null;
        }

        poolSyncRegistrationCoroutine = null;
    }

    void RegisterPoolSyncMessaging(NetworkManager networkManager)
    {
        if (networkManager == null || poolMessageHandlersRegistered)
            return;

        poolSyncNetworkManager = networkManager;
        poolSyncNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(
            PoolObjectiveStateMessageName,
            HandlePoolObjectiveStateMessage);
        poolSyncNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(
            PoolObjectiveStateRequestMessageName,
            HandlePoolObjectiveStateRequestMessage);
        poolSyncNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(
            PoolDirtCleanMessageName,
            HandlePoolDirtCleanMessage);
        poolSyncNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(
            PoolDirtCleanRequestMessageName,
            HandlePoolDirtCleanRequestMessage);
        poolSyncNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(
            PoolMandatoryStateMessageName,
            HandlePoolMandatoryStateMessage);
        poolSyncNetworkManager.OnClientConnectedCallback += HandlePoolSyncClientConnected;
        poolMessageHandlersRegistered = true;

        if (poolSyncNetworkManager.IsServer)
            SendKnownPoolStatesToConnectedClients();

        FlushPendingOutgoingPoolStates();
    }

    void UnregisterPoolSyncMessaging()
    {
        if (poolSyncRegistrationCoroutine != null)
        {
            StopCoroutine(poolSyncRegistrationCoroutine);
            poolSyncRegistrationCoroutine = null;
        }

        if (!poolMessageHandlersRegistered || poolSyncNetworkManager == null)
            return;

        if (poolSyncNetworkManager.CustomMessagingManager != null)
        {
            poolSyncNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(
                PoolObjectiveStateMessageName);
            poolSyncNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(
                PoolObjectiveStateRequestMessageName);
            poolSyncNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(
                PoolDirtCleanMessageName);
            poolSyncNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(
                PoolDirtCleanRequestMessageName);
            poolSyncNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(
                PoolMandatoryStateMessageName);
        }

        poolSyncNetworkManager.OnClientConnectedCallback -= HandlePoolSyncClientConnected;
        poolMessageHandlersRegistered = false;
        poolSyncNetworkManager = null;
    }

    void HandlePoolSyncClientConnected(ulong clientId)
    {
        if (!CanSendPoolObjectiveState())
            return;
        if (clientId == NetworkManager.ServerClientId)
            return;

        SendKnownPoolStates(clientId);
    }

    void SyncCleanedPoolState(SwimmingPoolObjective pool)
    {
        if (pool == null || pool.IsApplyingSynchronizedState)
            return;

        StartPoolSyncRegistration();

        if (!CanUsePoolSyncMessaging())
        {
            if (NetworkManager.Singleton != null)
                pendingOutgoingPoolStates.Add(pool);
            return;
        }

        if (poolSyncNetworkManager.IsServer)
        {
            SendPoolStateToConnectedClients(pool);
            return;
        }

        if (poolSyncNetworkManager.IsClient)
            SendPoolStateRequestToServer(pool);
    }

    void SyncPoolDirtClean(
        SwimmingPoolObjective pool,
        int dirtSpotIndex,
        Vector3 worldPoint,
        float worldRadius,
        float amount)
    {
        if (pool == null || pool.IsApplyingSynchronizedState)
            return;

        StartPoolSyncRegistration();

        if (!CanUsePoolSyncMessaging())
            return;

        if (poolSyncNetworkManager.IsServer)
        {
            SendPoolDirtCleanToConnectedClients(
                pool,
                dirtSpotIndex,
                worldPoint,
                worldRadius,
                amount,
                null);
            return;
        }

        if (poolSyncNetworkManager.IsClient)
        {
            SendPoolDirtCleanRequestToServer(
                pool,
                dirtSpotIndex,
                worldPoint,
                worldRadius,
                amount);
        }
    }

    void FlushPendingOutgoingPoolStates()
    {
        if (!CanUsePoolSyncMessaging() || pendingOutgoingPoolStates.Count == 0)
            return;

        SwimmingPoolObjective[] pendingPools =
            new SwimmingPoolObjective[pendingOutgoingPoolStates.Count];
        pendingOutgoingPoolStates.CopyTo(pendingPools);
        pendingOutgoingPoolStates.Clear();

        for (int i = 0; i < pendingPools.Length; i++)
            SyncCleanedPoolState(pendingPools[i]);
    }

    void SendPoolDirtCleanToConnectedClients(
        SwimmingPoolObjective pool,
        int dirtSpotIndex,
        Vector3 worldPoint,
        float worldRadius,
        float amount,
        ulong? excludedClientId)
    {
        if (pool == null || !CanSendPoolObjectiveState())
            return;

        for (int i = 0; i < poolSyncNetworkManager.ConnectedClientsIds.Count; i++)
        {
            ulong clientId = poolSyncNetworkManager.ConnectedClientsIds[i];
            if (clientId == NetworkManager.ServerClientId)
                continue;
            if (excludedClientId.HasValue && clientId == excludedClientId.Value)
                continue;

            SendPoolDirtClean(
                clientId,
                pool.SyncId,
                dirtSpotIndex,
                worldPoint,
                worldRadius,
                amount);
        }
    }

    void SendPoolDirtClean(
        ulong clientId,
        int poolSyncId,
        int dirtSpotIndex,
        Vector3 worldPoint,
        float worldRadius,
        float amount)
    {
        if (!CanSendPoolObjectiveState())
            return;

        FastBufferWriter writer = new FastBufferWriter(40, Allocator.Temp);
        try
        {
            WritePoolDirtCleanPayload(
                ref writer,
                poolSyncId,
                dirtSpotIndex,
                worldPoint,
                worldRadius,
                amount);

            poolSyncNetworkManager.CustomMessagingManager.SendNamedMessage(
                PoolDirtCleanMessageName,
                clientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }
        finally
        {
            writer.Dispose();
        }
    }

    void SendPoolDirtCleanRequestToServer(
        SwimmingPoolObjective pool,
        int dirtSpotIndex,
        Vector3 worldPoint,
        float worldRadius,
        float amount)
    {
        if (pool == null ||
            !CanUsePoolSyncMessaging() ||
            !poolSyncNetworkManager.IsClient ||
            poolSyncNetworkManager.IsServer)
        {
            return;
        }

        FastBufferWriter writer = new FastBufferWriter(40, Allocator.Temp);
        try
        {
            WritePoolDirtCleanPayload(
                ref writer,
                pool.SyncId,
                dirtSpotIndex,
                worldPoint,
                worldRadius,
                amount);

            poolSyncNetworkManager.CustomMessagingManager.SendNamedMessage(
                PoolDirtCleanRequestMessageName,
                NetworkManager.ServerClientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }
        finally
        {
            writer.Dispose();
        }
    }

    void HandlePoolDirtCleanMessage(
        ulong senderClientId,
        FastBufferReader messagePayload)
    {
        if (!CanUsePoolSyncMessaging() ||
            poolSyncNetworkManager.IsServer ||
            senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        int poolSyncId;
        int dirtSpotIndex;
        Vector3 worldPoint;
        float worldRadius;
        float amount;
        ReadPoolDirtCleanPayload(
            ref messagePayload,
            out poolSyncId,
            out dirtSpotIndex,
            out worldPoint,
            out worldRadius,
            out amount);

        ApplyPoolDirtCleanNetworkState(
            poolSyncId,
            dirtSpotIndex,
            worldPoint,
            worldRadius,
            amount);
    }

    void HandlePoolDirtCleanRequestMessage(
        ulong senderClientId,
        FastBufferReader messagePayload)
    {
        if (!CanSendPoolObjectiveState() ||
            senderClientId == NetworkManager.ServerClientId)
        {
            return;
        }

        int poolSyncId;
        int dirtSpotIndex;
        Vector3 worldPoint;
        float worldRadius;
        float amount;
        ReadPoolDirtCleanPayload(
            ref messagePayload,
            out poolSyncId,
            out dirtSpotIndex,
            out worldPoint,
            out worldRadius,
            out amount);

        if (!IsValidPoolCleanPayload(worldPoint, worldRadius, amount))
            return;

        SwimmingPoolObjective pool;
        if (TryFindPoolBySyncId(poolSyncId, out pool))
        {
            if (pool.IsCleaningLocked)
                return;

            ApplyPoolDirtCleanNetworkState(
                poolSyncId,
                dirtSpotIndex,
                worldPoint,
                worldRadius,
                amount);

            SendPoolDirtCleanToConnectedClients(
                pool,
                dirtSpotIndex,
                worldPoint,
                worldRadius,
                amount,
                senderClientId);

            if (pool.IsCleaned)
                SendPoolStateToConnectedClients(pool);
        }
    }

    void ApplyPoolDirtCleanNetworkState(
        int poolSyncId,
        int dirtSpotIndex,
        Vector3 worldPoint,
        float worldRadius,
        float amount)
    {
        if (!IsValidPoolCleanPayload(worldPoint, worldRadius, amount))
            return;

        SwimmingPoolObjective pool;
        if (!TryFindPoolBySyncId(poolSyncId, out pool))
            return;

        pool.ApplySynchronizedDirtCleanAtWorldPoint(
            dirtSpotIndex,
            worldPoint,
            worldRadius,
            amount);
    }

    static void WritePoolDirtCleanPayload(
        ref FastBufferWriter writer,
        int poolSyncId,
        int dirtSpotIndex,
        Vector3 worldPoint,
        float worldRadius,
        float amount)
    {
        writer.WriteValueSafe(poolSyncId);
        writer.WriteValueSafe(dirtSpotIndex);
        writer.WriteValueSafe(worldPoint.x);
        writer.WriteValueSafe(worldPoint.y);
        writer.WriteValueSafe(worldPoint.z);
        writer.WriteValueSafe(worldRadius);
        writer.WriteValueSafe(amount);
    }

    static void ReadPoolDirtCleanPayload(
        ref FastBufferReader reader,
        out int poolSyncId,
        out int dirtSpotIndex,
        out Vector3 worldPoint,
        out float worldRadius,
        out float amount)
    {
        float x;
        float y;
        float z;

        reader.ReadValueSafe(out poolSyncId);
        reader.ReadValueSafe(out dirtSpotIndex);
        reader.ReadValueSafe(out x);
        reader.ReadValueSafe(out y);
        reader.ReadValueSafe(out z);
        reader.ReadValueSafe(out worldRadius);
        reader.ReadValueSafe(out amount);

        worldPoint = new Vector3(x, y, z);
    }

    static bool IsValidPoolCleanPayload(
        Vector3 worldPoint,
        float worldRadius,
        float amount)
    {
        return IsFiniteVector3(worldPoint) &&
            IsFiniteFloat(worldRadius) &&
            IsFiniteFloat(amount) &&
            worldRadius > 0f &&
            amount > 0f;
    }

    static bool IsFiniteVector3(Vector3 value)
    {
        return IsFiniteFloat(value.x) &&
            IsFiniteFloat(value.y) &&
            IsFiniteFloat(value.z);
    }

    static bool IsFiniteFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    bool CanUsePoolSyncMessaging()
    {
        return poolMessageHandlersRegistered &&
            poolSyncNetworkManager != null &&
            poolSyncNetworkManager.IsListening &&
            poolSyncNetworkManager.CustomMessagingManager != null;
    }

    bool CanSendPoolObjectiveState()
    {
        return CanUsePoolSyncMessaging() && poolSyncNetworkManager.IsServer;
    }

    void SendKnownPoolStatesToConnectedClients()
    {
        if (!CanSendPoolObjectiveState())
            return;

        for (int i = 0; i < poolSyncNetworkManager.ConnectedClientsIds.Count; i++)
        {
            ulong clientId = poolSyncNetworkManager.ConnectedClientsIds[i];
            if (clientId == NetworkManager.ServerClientId)
                continue;

            SendKnownPoolStates(clientId);
        }
    }

    void SendKnownPoolStates(ulong clientId)
    {
        RegisterKnownPoolObjectives();

        foreach (SwimmingPoolObjective pool in registeredPools)
        {
            if (pool == null)
                continue;

            SendPoolState(clientId, pool.SyncId, pool.SyncState);
            SendPoolMandatoryState(clientId, pool.SyncId, pool.RequiredForLevelCompletion);
        }
    }

    void SendPoolStateToConnectedClients(SwimmingPoolObjective pool)
    {
        if (pool == null || !CanSendPoolObjectiveState())
            return;

        for (int i = 0; i < poolSyncNetworkManager.ConnectedClientsIds.Count; i++)
        {
            ulong clientId = poolSyncNetworkManager.ConnectedClientsIds[i];
            if (clientId == NetworkManager.ServerClientId)
                continue;

            SendPoolState(clientId, pool.SyncId, pool.SyncState);
        }
    }

    void SendPoolState(ulong clientId, int poolSyncId, byte poolState)
    {
        if (!CanSendPoolObjectiveState())
            return;

        FastBufferWriter writer = new FastBufferWriter(8, Allocator.Temp);
        try
        {
            writer.WriteValueSafe(poolSyncId);
            writer.WriteValueSafe(poolState);

            poolSyncNetworkManager.CustomMessagingManager.SendNamedMessage(
                PoolObjectiveStateMessageName,
                clientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }
        finally
        {
            writer.Dispose();
        }
    }

    void SendPoolStateRequestToServer(SwimmingPoolObjective pool)
    {
        if (pool == null ||
            !CanUsePoolSyncMessaging() ||
            !poolSyncNetworkManager.IsClient ||
            poolSyncNetworkManager.IsServer)
        {
            return;
        }

        FastBufferWriter writer = new FastBufferWriter(8, Allocator.Temp);
        try
        {
            writer.WriteValueSafe(pool.SyncId);
            writer.WriteValueSafe(pool.SyncState);

            poolSyncNetworkManager.CustomMessagingManager.SendNamedMessage(
                PoolObjectiveStateRequestMessageName,
                NetworkManager.ServerClientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }
        finally
        {
            writer.Dispose();
        }
    }

    void HandlePoolObjectiveStateMessage(
        ulong senderClientId,
        FastBufferReader messagePayload)
    {
        if (!CanUsePoolSyncMessaging() ||
            poolSyncNetworkManager.IsServer ||
            senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        int poolSyncId;
        byte poolState;
        messagePayload.ReadValueSafe(out poolSyncId);
        messagePayload.ReadValueSafe(out poolState);

        ApplyPoolNetworkState(poolSyncId, poolState);
    }

    void HandlePoolObjectiveStateRequestMessage(
        ulong senderClientId,
        FastBufferReader messagePayload)
    {
        if (!CanSendPoolObjectiveState() ||
            senderClientId == NetworkManager.ServerClientId)
        {
            return;
        }

        int poolSyncId;
        byte poolState;
        messagePayload.ReadValueSafe(out poolSyncId);
        messagePayload.ReadValueSafe(out poolState);

        if ((SwimmingPoolObjectiveState)poolState != SwimmingPoolObjectiveState.Clean)
            return;

        SwimmingPoolObjective pool;
        if (TryFindPoolBySyncId(poolSyncId, out pool))
            pool.ApplySynchronizedState(poolState);
        else
            pendingPoolNetworkStates[poolSyncId] = poolState;

        if (pool != null)
            SendPoolStateToConnectedClients(pool);
    }

    void ApplyPoolNetworkState(int poolSyncId, byte poolState)
    {
        SwimmingPoolObjective pool;
        if (TryFindPoolBySyncId(poolSyncId, out pool))
        {
            pool.ApplySynchronizedState(poolState);
            return;
        }

        pendingPoolNetworkStates[poolSyncId] = poolState;
    }

    void ApplyPendingPoolNetworkState(SwimmingPoolObjective pool)
    {
        if (pool == null)
            return;

        byte pendingState;
        if (!pendingPoolNetworkStates.TryGetValue(pool.SyncId, out pendingState))
            return;

        pendingPoolNetworkStates.Remove(pool.SyncId);
        pool.ApplySynchronizedState(pendingState);
    }

    bool TryFindPoolBySyncId(
        int poolSyncId,
        out SwimmingPoolObjective foundPool)
    {
        foreach (SwimmingPoolObjective pool in registeredPools)
        {
            if (pool != null && pool.SyncId == poolSyncId)
            {
                foundPool = pool;
                return true;
            }
        }

        SwimmingPoolObjective[] pools =
            FindObjectsByType<SwimmingPoolObjective>(FindObjectsInactive.Include);
        for (int i = 0; i < pools.Length; i++)
        {
            SwimmingPoolObjective pool = pools[i];
            if (pool == null)
                continue;

            RegisterPoolObjective(pool);
            if (pool.SyncId == poolSyncId)
            {
                foundPool = pool;
                return true;
            }
        }

        foundPool = null;
        return false;
    }

    void UpdateObjectiveHUD()
    {
        AutoBindHudFields();
        BindCleanGoalUI();
        UpdateCleanGoalUI();

        if (objectiveText != null)
        {
            if (levelCompleted)
                objectiveText.text = Localized("objective.returnToSubmarine", returnToSubmarineObjectiveLabel);
            else if (!WaterValveActivated)
                objectiveText.text = Localized("objective.findValve", findWaterValveObjectiveLabel);
            else
                objectiveText.text = Localized("objective.cleanPools", activeObjectiveLabel);
        }

        float totalCleanForHud = WaterValveActivated ? currentCleanPercent : 0f;
        float poolCleanForHud = WaterValveActivated ? CalculateRequiredPoolCleanPercent() : 0f;
        CalculateCurrentPoolCleanPercent();
        int cleanPercent = Mathf.RoundToInt(totalCleanForHud * 100f);
        int poolCleanPercent = Mathf.RoundToInt(poolCleanForHud * 100f);

        if (cleaningProgressText != null)
            cleaningProgressText.text = string.Format(
                Localized("hud.totalCleaningProgress", cleaningProgressFormat),
                cleanPercent);

        if (cleaningProgressBar != null)
            cleaningProgressBar.value = totalCleanForHud;

        if (cleaningProgressFill != null)
            cleaningProgressFill.fillAmount = totalCleanForHud;

        if (currentPoolProgressText != null)
            currentPoolProgressText.text = string.Format(
                Localized("hud.poolCleaningProgress", currentPoolProgressFormat),
                poolCleanPercent);

        if (currentPoolProgressBar != null)
            currentPoolProgressBar.value = poolCleanForHud;

        if (currentPoolProgressFill != null)
            currentPoolProgressFill.fillAmount = poolCleanForHud;

        if (poolCounterText != null)
            poolCounterText.text = string.Format(
                Localized("hud.pools", poolCounterFormat),
                cleanedRequiredPoolCount,
                requiredPoolCount);

        if (levelInfoText != null)
            levelInfoText.text = GetLevelInfoText();

        if (progressText == null) return;

        if (levelCompleted)
        {
            progressText.text =
                Localized("objective.returnToSubmarine", returnToSubmarineObjectiveLabel);
            return;
        }

        if (!WaterValveActivated)
        {
            progressText.text = Localized("objective.turnValve", findWaterValveProgressLabel);
            return;
        }

        int requiredPercent = Mathf.RoundToInt(requiredCleanPercent * 100f);
        string cleanText = requireTotalCleanPercent
            ? $"{Localized("objective.cleaning", "Cleaning")} {cleanPercent}% / {requiredPercent}%"
            : Localized("objective.cleanPools", activeObjectiveLabel);

        string finalText = string.Empty;
        if (requireFinalRoomDiscovered)
        {
            finalText = finalRoomDiscovered
                ? $" - {Localized("objective.exitFound", "Exit found")}"
                : $" - {Localized("objective.findExit", "Find the exit")}";
        }

        string poolText = requireAllRequiredPoolsClean && requiredPoolCount > 0
            ? $" - {Localized("objective.poolCleaning", "Pool Cleaning")} {poolCleanPercent}%" +
              $" - {Localized("objective.pools", "Pools")} {cleanedRequiredPoolCount}/{requiredPoolCount}"
            : string.Empty;

        string hazardText = BuildDiscoveredPoolHazardObjectiveText();

        progressText.text =
            $"{cleanText}{poolText}{hazardText} - {Localized("objective.rooms", "Rooms")} {discoveredRoomCount}{finalText}";
    }

    string BuildDiscoveredPoolHazardObjectiveText()
    {
        string text = string.Empty;

        FungalSwimmingPoolMechanic[] fungalPools =
            FindObjectsByType<FungalSwimmingPoolMechanic>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        int discoveredFungalMushrooms = 0;
        for (int i = 0; i < fungalPools.Length; i++)
        {
            FungalSwimmingPoolMechanic fungalPool = fungalPools[i];
            if (fungalPool == null || !IsPoolRoomDiscovered(fungalPool.transform))
                continue;

            discoveredFungalMushrooms += fungalPool.ActiveHarmfulMushroomCount;
        }
        if (discoveredFungalMushrooms > 0)
        {
            text += " - " + string.Format(
                Localized("objective.fungalPool", fungalPoolObjectiveFormat),
                discoveredFungalMushrooms);
        }

        ElectricSwimmingPoolMechanic[] electricPools =
            FindObjectsByType<ElectricSwimmingPoolMechanic>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        for (int i = 0; i < electricPools.Length; i++)
        {
            ElectricSwimmingPoolMechanic electricPool = electricPools[i];
            if (electricPool == null || !IsPoolRoomDiscovered(electricPool.transform))
                continue;

            if (electricPool.IsPowered)
            {
                text += " - " + Localized(
                    "objective.electricPoolPowered",
                    electricPoolObjectivePowered);
            }
            else
            {
                text += " - " + string.Format(
                    Localized(
                        "objective.electricPoolSafe",
                        electricPoolObjectiveSafeFormat),
                    Mathf.CeilToInt(electricPool.PowerReturnSeconds));
            }
        }

        return text;
    }

    bool IsPoolRoomDiscovered(Transform poolTransform)
    {
        if (poolTransform == null)
            return false;

        RoomDefinition room = poolTransform.GetComponentInParent<RoomDefinition>();
        return room != null && discoveredRoomSet.Contains(room);
    }

    string Localized(string key, string fallback)
    {
        return GameLocalization.Translate(key, fallback);
    }

    string GetLevelInfoText()
    {
        string regionName = RegionRunState.HasSelectedRegion
            ? RegionRunState.RegionName
            : string.Empty;
        int phaseNumber = RegionRunState.HasSelectedRegion
            ? RegionRunState.PhaseNumber
            : levelNumber;

        if (!string.IsNullOrWhiteSpace(regionName))
            return string.Format(regionLevelInfoFormat, phaseNumber, regionName);

        return string.Format(levelInfoFormat, phaseNumber);
    }

    void AutoBindHudFields()
    {
        if (!autoBindNewHudFields)
            return;

        if (cleaningProgressText != null &&
            cleaningProgressBar != null &&
            currentPoolProgressText != null &&
            currentPoolProgressBar != null &&
            poolCounterText != null &&
            levelInfoText != null)
        {
            return;
        }

        if (cleaningProgressBar == null)
        {
            Slider[] sliders = FindObjectsByType<Slider>(FindObjectsInactive.Include);
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

        if (currentPoolProgressBar == null)
        {
            Slider[] sliders = FindObjectsByType<Slider>(FindObjectsInactive.Include);
            for (int i = 0; i < sliders.Length; i++)
            {
                Slider slider = sliders[i];
                if (slider != null &&
                    (slider.gameObject.name.Contains("Current Pool") ||
                     slider.gameObject.name.Contains("Pool Progress")))
                {
                    currentPoolProgressBar = slider;
                    break;
                }
            }
        }

        TMP_Text[] texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null) continue;

            string objectName = text.gameObject != null
                ? text.gameObject.name
                : string.Empty;
            string parentName = text.transform.parent != null
                ? text.transform.parent.name
                : string.Empty;
            string textValue = text.text ?? string.Empty;

            if (cleaningProgressText == null &&
                (objectName.Contains("Cleaning") ||
                 parentName.Contains("Progress Bar") ||
                 textValue.Contains("Cleaning")))
            {
                cleaningProgressText = text;
            }
            else if (currentPoolProgressText == null &&
                (objectName.Contains("Current Pool") ||
                 parentName.Contains("Current Pool") ||
                 objectName.Contains("Pool Progress") ||
                 textValue.Contains("Current Pool")))
            {
                currentPoolProgressText = text;
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

    void BindCleanGoalUI()
    {
        if (autoFindCleanGoalUI)
        {
            if (cleanGoalSlider == null)
                cleanGoalSlider = FindSliderByName(cleanGoalObjectName);

            if (cleanGoalText == null)
                cleanGoalText = FindCleanGoalText();
        }

        if (autoFindPoolCleanGoalUI)
        {
            if (poolCleanGoalSlider == null)
                poolCleanGoalSlider = FindSliderByName(poolCleanGoalObjectName);

            if (poolCleanGoalText == null)
                poolCleanGoalText = FindPoolCleanGoalText();
        }
    }

    Slider FindSliderByName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;

        Slider[] sliders = FindObjectsByType<Slider>(FindObjectsInactive.Include);
        for (int i = 0; i < sliders.Length; i++)
        {
            Slider slider = sliders[i];
            if (slider == null)
                continue;

            if (string.Equals(slider.name, objectName, StringComparison.Ordinal) ||
                string.Equals(slider.name, objectName, StringComparison.OrdinalIgnoreCase))
            {
                return slider;
            }
        }

        return null;
    }

    TMP_Text FindCleanGoalText()
    {
        if (cleanGoalSlider != null)
        {
            TMP_Text[] childTexts =
                cleanGoalSlider.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text childText =
                FindTextByName(childTexts, cleanGoalTextObjectName);
            if (childText != null)
                return childText;
        }

        TMP_Text[] texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
        return FindTextByName(texts, cleanGoalTextObjectName);
    }

    TMP_Text FindPoolCleanGoalText()
    {
        if (poolCleanGoalSlider != null)
        {
            TMP_Text[] childTexts =
                poolCleanGoalSlider.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text childText = FindTextByNameWithFallbacks(childTexts, poolCleanGoalTextObjectName, "PGoalText", "PGoalTect");
            if (childText != null)
                return childText;

            if (childTexts != null && childTexts.Length > 0)
                return childTexts[0];
        }

        TMP_Text[] texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);
        return FindTextByNameWithFallbacks(texts, poolCleanGoalTextObjectName, "PGoalText", "PGoalTect");
    }

    TMP_Text FindTextByNameWithFallbacks(TMP_Text[] texts, params string[] names)
    {
        if (texts == null || names == null) return null;
        for (int n = 0; n < names.Length; n++)
        {
            string objectName = names[n];
            if (string.IsNullOrEmpty(objectName)) continue;
            TMP_Text found = FindTextByName(texts, objectName);
            if (found != null) return found;
        }
        return null;
    }

    TMP_Text FindTextByName(TMP_Text[] texts, string objectName)
    {
        if (texts == null || string.IsNullOrEmpty(objectName))
            return null;

        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null)
                continue;

            if (string.Equals(text.name, objectName, StringComparison.Ordinal) ||
                string.Equals(text.name, objectName, StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return null;
    }

    void UpdateCleanGoalUI()
    {
        float required = Mathf.Clamp01(requiredCleanPercent);
        float clean = WaterValveActivated ? currentCleanPercent : 0f;
        float normalizedGoal = required > 0f ? Mathf.Clamp01(clean / required) : 1f;
        bool shouldShow = !showCleanGoalOnlyAfterWaterValve || WaterValveActivated;

        if (cleanGoalSlider != null)
        {
            if (cleanGoalSlider.gameObject.activeSelf != shouldShow)
                cleanGoalSlider.gameObject.SetActive(shouldShow);

            cleanGoalSlider.minValue = 0f;
            cleanGoalSlider.maxValue = 1f;
            cleanGoalSlider.value = levelCompleted ? 1f : normalizedGoal;
        }

        if (cleanGoalText != null)
        {
            if (cleanGoalSlider == null &&
                cleanGoalText.gameObject.activeSelf != shouldShow)
            {
                cleanGoalText.gameObject.SetActive(shouldShow);
            }

            int cleanPercent = Mathf.RoundToInt(clean * 100f);
            int requiredPercent = Mathf.RoundToInt(required * 100f);
            cleanGoalText.text = $"{cleanPercent}% / {requiredPercent}%";
        }
    }

    void UpdatePoolCleanGoalUI()
    {
        if (poolCleanGoalSlider == null && poolCleanGoalText == null)
            return;

        SwimmingPoolObjective mostRecentPool = null;
        float newestCleanedTime = -1f;

        foreach (SwimmingPoolObjective pool in registeredPools)
        {
            if (pool == null) continue;
            if (pool.LastCleanedTime > newestCleanedTime)
            {
                newestCleanedTime = pool.LastCleanedTime;
                mostRecentPool = pool;
            }
        }

        bool isCurrentlyCleaningPool = mostRecentPool != null &&
            (Time.time - newestCleanedTime <= poolCleanGoalHideDelay);

        if (poolCleanGoalSlider != null)
        {
            if (poolCleanGoalSlider.gameObject.activeSelf != isCurrentlyCleaningPool)
                poolCleanGoalSlider.gameObject.SetActive(isCurrentlyCleaningPool);

            if (isCurrentlyCleaningPool)
            {
                poolCleanGoalSlider.minValue = 0f;
                poolCleanGoalSlider.maxValue = 1f;
                poolCleanGoalSlider.value = Mathf.Clamp01(mostRecentPool.CleanProgress);
            }
        }

        if (poolCleanGoalText != null)
        {
            if (poolCleanGoalSlider == null &&
                poolCleanGoalText.gameObject.activeSelf != isCurrentlyCleaningPool)
            {
                poolCleanGoalText.gameObject.SetActive(isCurrentlyCleaningPool);
            }

            if (isCurrentlyCleaningPool)
            {
                int poolPercent = Mathf.RoundToInt(mostRecentPool.CleanProgress * 100f);
                poolCleanGoalText.text = $"{poolPercent}%";
            }
        }
    }

    void SendPoolMandatoryStateToConnectedClients(SwimmingPoolObjective pool)
    {
        if (pool == null || !CanSendPoolObjectiveState())
            return;

        for (int i = 0; i < poolSyncNetworkManager.ConnectedClientsIds.Count; i++)
        {
            ulong clientId = poolSyncNetworkManager.ConnectedClientsIds[i];
            if (clientId == NetworkManager.ServerClientId)
                continue;

            SendPoolMandatoryState(clientId, pool.SyncId, pool.RequiredForLevelCompletion);
        }
    }

    void SendPoolMandatoryState(ulong clientId, int poolSyncId, bool isMandatory)
    {
        if (!CanSendPoolObjectiveState())
            return;

        FastBufferWriter writer = new FastBufferWriter(5, Allocator.Temp);
        try
        {
            writer.WriteValueSafe(poolSyncId);
            writer.WriteValueSafe(isMandatory);

            poolSyncNetworkManager.CustomMessagingManager.SendNamedMessage(
                PoolMandatoryStateMessageName,
                clientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }
        finally
        {
            writer.Dispose();
        }
    }

    void HandlePoolMandatoryStateMessage(
        ulong senderClientId,
        FastBufferReader messagePayload)
    {
        if (!CanUsePoolSyncMessaging() ||
            poolSyncNetworkManager.IsServer ||
            senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        int poolSyncId;
        bool isMandatory;
        messagePayload.ReadValueSafe(out poolSyncId);
        messagePayload.ReadValueSafe(out isMandatory);

        ApplyPoolMandatoryNetworkState(poolSyncId, isMandatory);
    }

    void ApplyPoolMandatoryNetworkState(int poolSyncId, bool isMandatory)
    {
        SwimmingPoolObjective pool;
        if (TryFindPoolBySyncId(poolSyncId, out pool))
        {
            pool.RequiredForLevelCompletion = isMandatory;
            UpdatePoolDebugCounts();
            RefreshObjectiveState();
            return;
        }

        pendingPoolMandatoryStates[poolSyncId] = isMandatory;
    }

    void ApplyPendingPoolMandatoryState(SwimmingPoolObjective pool)
    {
        if (pool == null)
            return;

        bool pendingState;
        if (!pendingPoolMandatoryStates.TryGetValue(pool.SyncId, out pendingState))
            return;

        pendingPoolMandatoryStates.Remove(pool.SyncId);
        pool.RequiredForLevelCompletion = pendingState;
    }
}
