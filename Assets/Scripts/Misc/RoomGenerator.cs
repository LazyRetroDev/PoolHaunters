using UnityEngine;
using Unity.AI.Navigation;
using Unity.Collections;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class RoomGenerator : MonoBehaviour
{
    const string ClosedDoorChildName = "ClosedDoor";
    const string GeneratedMapSnapshotMessageName = "PoolHaunters.GeneratedMapSnapshot";
    const string GeneratedMapRequestMessageName = "PoolHaunters.GeneratedMapRequest";

    class RoomPlacement
    {
        public GameObject room;
        public Vector2Int cell;
        public Bounds bounds;
    }

    class RoomGenerationSnapshot
    {
        public int spawnedRoomCount;
        public int registeredSurfaceCount;
        public int generatedRoomCount;
        public bool initialEnemySpawned;
        public bool pendingInitialTimeCamperSpawn;
        public bool mapConsolidated;
        public Transform lastExitPoint;
        public List<RoomConnector> openConnectors;
        public List<PendingRoomContentSpawn> pendingRoomContentSpawns;
        public HashSet<GameObject> contentSpawnedRooms;
        public Dictionary<GameObject, int> generatedPrefabCounts;
        public Dictionary<GameObject, int> lastGeneratedPrefabIndices;
        public Dictionary<GameObject, int> generatedPrefabIndicesByRoom;
        public Dictionary<GameObject, RoomPlacement> placementsByRoom;
        public Dictionary<Vector2Int, RoomPlacement> placementsByCell;
        public Dictionary<RoomConnector, Vector2Int> connectorTargetCells;
        public Dictionary<RoomConnector, NavMeshLink> navMeshLinksByConnector;
        public Dictionary<GameObject, RoomDebugInfo> roomDebugInfoByRoom;
        public List<ConnectorDebugConnection> debugBranchConnections;
    }

    class RoomDebugInfo
    {
        public int roomIndex;
        public int branchNumber;
        public int branchRoomNumber;
        public bool isStart;
        public bool isFinal;
        public Vector2Int cell;
    }

    class GeneratedMapRoomSnapshot
    {
        public int roomIndex;
        public int prefabIndex;
        public Vector3 position;
        public Quaternion rotation;
        public Vector2Int cell;
        public int[] connectorStates;
    }

    class PendingRoomContentSpawn
    {
        public GameObject room;
        public int roomIndex;
    }

    class ConnectorDebugConnection
    {
        public RoomConnector first;
        public RoomConnector second;
    }

    class FullMapGenerationStats
    {
        public int requiredBranchCount;
        public int requestedBranchCount;
        public int completedBranchCount;
        public int branchBuildAttempts;
    }

    class BranchGenerationReport
    {
        public int branchNumber;
        public int requestedRoomCount;
        public int generatedRoomCount;
        public int futureBranchStartsNeeded;
        public bool completed;
        public string startConnectorName;
        public string finalRoomName;
        public string failureReason;
        public readonly List<string> rooms = new List<string>();
    }

    class FullMapGenerationReport
    {
        public int attemptNumber;
        public int totalAttempts;
        public int seed;
        public int requiredBranchCount;
        public int requestedBranchCount;
        public int completedBranchCount;
        public int branchBuildAttempts;
        public int adjacentBranchConnectionCount;
        public int generatedRoomCount;
        public int finalRoomCount;
        public int closedConnectorCount;
        public int connectedConnectorPairCount;
        public int navMeshLinkCount;
        public bool accepted;
        public string startRoomName;
        public string validationSummary;
        public readonly List<BranchGenerationReport> branches =
            new List<BranchGenerationReport>();
        public readonly Dictionary<string, int> rejectionCounts =
            new Dictionary<string, int>();
    }

    class MapValidationResult
    {
        private readonly List<string> failures = new List<string>();

        public bool IsValid
        {
            get { return failures.Count == 0; }
        }

        public void Fail(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                failures.Add(message);
        }

        public string GetSummary()
        {
            if (IsValid)
                return "valid";

            return string.Join("; ", failures.ToArray());
        }
    }

    enum RoomGenerationRole
    {
        Any,
        BranchMiddle,
        BranchFinal
    }

    [Header("Generation Profile")]
    [SerializeField] private RoomGenerationProfile generationProfile;
    [SerializeField] private bool applyGenerationProfileOnAwake = true;

    [Header("Rooms")]
    public GameObject[] roomPrefabs;
    public int startingRoomCount = 2;
    public int maxGeneratedRooms = 0;

    [Header("Full Map Generation")]
    public bool generateFullMapOnStart;

    [Header("Required Rooms")]
    [SerializeField] private bool requirePoolRoomsInFullMap = true;
    [Min(0)]
    [SerializeField] private int minimumRequiredPoolRooms = 1;

    [Min(1)]
    public int minimumBranchCount = 3;

    [Min(1)]
    public int maximumBranchCount = 4;

    [Min(1)]
    [Tooltip("Total rooms generated per branch, including the final room.")]
    public int minimumRoomsPerBranch = 3;

    [Min(1)]
    [Tooltip("Total rooms generated per branch, including the final room.")]
    public int maximumRoomsPerBranch = 6;

    [Min(1)]
    public int branchGenerationAttempts = 8;

    [Tooltip("Forces early branch rooms to leave extra exits until the minimum branch count is guaranteed.")]
    public bool guaranteeMinimumBranchCount = true;

    [Tooltip("Allows the same Final room prefab to close multiple branches in full-map generation.")]
    public bool allowRepeatingFinalRoomsForBranches = true;

    [Header("Branch Connections")]
    public bool connectAdjacentBranches = true;

    [Range(0f, 1f)]
    public float adjacentBranchConnectionChance = 0.35f;

    [Tooltip("Use 0 for no maximum.")]
    [Min(0)]
    public int maximumAdjacentBranchConnections = 0;

    [Tooltip("Maximum world distance allowed between DoorPoints when connecting two already generated branches.")]
    [Min(0.1f)]
    public float maximumAdjacentBranchConnectionDoorDistance = 4.5f;

    [Tooltip("Reject maps where a Final room can be reached from the starting room in fewer steps than minimumRoomsPerBranch.")]
    public bool enforceFinalRoomMinimumDistance = true;

    [Header("Full Map Validation")]
    public bool validateFullMapAfterGeneration = true;

    [Min(1)]
    public int fullMapGenerationAttempts = 20;

    public bool validateClosedDoorObjects = true;
    public bool validateConnectedNavMeshLinks = true;

    [Header("Generation Debug")]
    public bool logGenerationReport = true;
    public bool logRejectedFullMapAttempts = true;
    public bool renameGeneratedRoomsForDebug = true;
    public bool drawGeneratedMapGizmos = true;
    public bool drawGeneratedMapLabels = true;
    public bool drawClosedConnectorGizmos = true;
    public bool drawBranchConnectionGizmos = true;

    [Min(0.1f)]
    public float generationDebugMarkerSize = 1.25f;

    [Min(0f)]
    public float generationDebugLabelHeight = 2.5f;

    [TextArea(8, 30)]
    [SerializeField] private string lastGenerationReport;

    [Tooltip("Keep enabled while the player can walk back through already generated rooms.")]
    public bool keepGeneratedRoomsForBacktracking = true;

    [Tooltip("Only used when backtracking preservation is disabled. Use 0 to keep every spawned room.")]
    [Min(0)]
    public int roomsToKeep = 0;

    public int seed = 0;

    [Header("Run Seed")]
    public bool useSelectedRunSeed = true;
    public bool randomizeSeedWhenNoRunSelected = false;

    [Header("Multiplayer Sync")]
    public bool synchronizeGeneratedMapToClients = true;
    public bool teleportClientPlayerAfterMapSync = true;
    [SerializeField] private string playerSpawnName = "PlayerSpawn";

    [Header("Enemy Setup")]
    public bool spawnTimeCamperAfterStartingRooms = true;
    [SerializeField] private RoomEnemySpawner enemySpawner;

    [Header("Room Resources")]
    [SerializeField] private RoomResourceSpawner resourceSpawner;

    [Header("Water Valve Objective")]
    [SerializeField] private bool spawnWaterValveOnGeneratedMap = true;
    [SerializeField] private GameObject waterValvePrefab;
    [SerializeField, Min(0f)] private float waterValveWallHeight = 1.25f;
    [SerializeField, Min(0f)] private float waterValveWallInset = 0.15f;
    [SerializeField, Min(0f)] private float waterValveSidePadding = 1.25f;
    [SerializeField, Min(1)] private int waterValveWallProbeAttempts = 16;
    [SerializeField] private Vector3 waterValveRotationOffset;

    [Header("Run Progression")]
    [SerializeField] private RoomProgressionController progression;

    [Header("Placement")]
    public bool useGridOccupancy = true;
    public bool useBoundsOverlapCheck = true;

    [Min(0f)]
    public float roomBoundsInset = 0.25f;

    [Min(1)]
    public int placementAttempts = 8;

    [Header("Doorway Validation")]
    public bool validateConnectedDoorwayClearance = true;

    [Min(0.1f)]
    public float doorwayClearanceWidth = 1.2f;

    [Min(0.1f)]
    public float doorwayClearanceHeight = 4.5f;

    [Min(0.1f)]
    public float doorwayClearanceDepth = 2f;

    public bool ignoreFloorLevelDoorwayBlockers = true;

    [Min(0f)]
    public float doorwayFloorBlockerTolerance = 0.1f;

    public LayerMask doorwayIgnoredLayers = 1 << 7;

    public LayerMask doorwayBlockingLayers = ~0;

    [Header("NavMesh Links")]
    public bool createNavMeshLinksBetweenRooms = true;

    [Min(0.1f)]
    public float navMeshLinkWidth = 2f;

    [Min(0f)]
    public float navMeshLinkWorldHeight = 0.75f;

    [Min(0.1f)]
    public float navMeshLinkHalfLength = 2f;

    private readonly List<GameObject> spawnedRooms = new List<GameObject>();
    private readonly List<NavMeshSurface> registeredSurfaces = new List<NavMeshSurface>();
    private readonly List<RoomConnector> openConnectors = new List<RoomConnector>();
    private readonly Dictionary<GameObject, int> generatedPrefabCounts =
        new Dictionary<GameObject, int>();
    private readonly Dictionary<GameObject, int> lastGeneratedPrefabIndices =
        new Dictionary<GameObject, int>();
    private readonly Dictionary<GameObject, int> generatedPrefabIndicesByRoom =
        new Dictionary<GameObject, int>();
    private readonly Dictionary<GameObject, RoomPlacement> placementsByRoom =
        new Dictionary<GameObject, RoomPlacement>();
    private readonly Dictionary<Vector2Int, RoomPlacement> placementsByCell =
        new Dictionary<Vector2Int, RoomPlacement>();
    private readonly Dictionary<RoomConnector, Vector2Int> connectorTargetCells =
        new Dictionary<RoomConnector, Vector2Int>();
    private readonly Dictionary<RoomConnector, NavMeshLink> navMeshLinksByConnector =
        new Dictionary<RoomConnector, NavMeshLink>();
    private readonly Dictionary<GameObject, RoomDebugInfo> roomDebugInfoByRoom =
        new Dictionary<GameObject, RoomDebugInfo>();
    private readonly List<ConnectorDebugConnection> debugBranchConnections =
        new List<ConnectorDebugConnection>();
    private readonly List<PendingRoomContentSpawn> pendingRoomContentSpawns =
        new List<PendingRoomContentSpawn>();
    private readonly HashSet<GameObject> contentSpawnedRooms =
        new HashSet<GameObject>();

    private Transform navMeshLinkRoot;
    private Transform lastExitPoint;
    private int generatedRoomCount;
    private bool initialEnemySpawned;
    private bool pendingInitialTimeCamperSpawn;
    private bool mapConsolidated;
    private bool isGeneratingFullMap;
    private FullMapGenerationReport currentGenerationReport;
    private FullMapGenerationReport lastCompletedGenerationReport;
    private BranchGenerationReport currentBranchReport;
    private NetworkManager mapSyncNetworkManager;
    private Coroutine initialGenerationCoroutine;
    private Coroutine mapSyncRegistrationCoroutine;
    private Coroutine roomContentFlushCoroutine;
    private bool mapMessageHandlersRegistered;
    private bool generatedMapSnapshotReady;
    private bool clientPlayerTeleportedAfterInitialMapSync;
    private GameObject spawnedWaterValve;

    void OnEnable()
    {
        StartMapSyncRegistration();
    }

    void OnDisable()
    {
        StopInitialGenerationCoroutine();
        StopRoomContentFlushCoroutine();
        UnregisterMapSyncMessaging();
    }

    void Awake()
    {
        if (resourceSpawner == null)
            resourceSpawner = GetComponent<RoomResourceSpawner>();

        if (enemySpawner == null)
            enemySpawner = GetComponent<RoomEnemySpawner>();

        if (progression == null)
            progression = GetComponent<RoomProgressionController>();

        if (applyGenerationProfileOnAwake)
            ApplyGenerationProfile();
    }

    [ContextMenu("Apply Generation Profile")]
    public void ApplyGenerationProfile()
    {
        if (generationProfile == null)
            return;

        ApplyGenerationProfile(generationProfile);
    }

    public void ApplyGenerationProfile(RoomGenerationProfile profile)
    {
        if (profile == null)
            return;

        if (profile.overrideRoomPrefabs)
            roomPrefabs = profile.roomPrefabs;

        startingRoomCount = profile.startingRoomCount;
        maxGeneratedRooms = profile.maxGeneratedRooms;

        generateFullMapOnStart = profile.generateFullMapOnStart;
        minimumBranchCount = profile.minimumBranchCount;
        maximumBranchCount = profile.maximumBranchCount;
        minimumRoomsPerBranch = profile.minimumRoomsPerBranch;
        maximumRoomsPerBranch = profile.maximumRoomsPerBranch;
        branchGenerationAttempts = profile.branchGenerationAttempts;
        guaranteeMinimumBranchCount = profile.guaranteeMinimumBranchCount;
        allowRepeatingFinalRoomsForBranches =
            profile.allowRepeatingFinalRoomsForBranches;
        connectAdjacentBranches = profile.connectAdjacentBranches;
        adjacentBranchConnectionChance = profile.adjacentBranchConnectionChance;
        maximumAdjacentBranchConnections =
            profile.maximumAdjacentBranchConnections;
        maximumAdjacentBranchConnectionDoorDistance =
            profile.maximumAdjacentBranchConnectionDoorDistance;
        enforceFinalRoomMinimumDistance =
            profile.enforceFinalRoomMinimumDistance;

        validateFullMapAfterGeneration = profile.validateFullMapAfterGeneration;
        fullMapGenerationAttempts = profile.fullMapGenerationAttempts;
        validateClosedDoorObjects = profile.validateClosedDoorObjects;
        validateConnectedNavMeshLinks = profile.validateConnectedNavMeshLinks;

        keepGeneratedRoomsForBacktracking =
            profile.keepGeneratedRoomsForBacktracking;
        roomsToKeep = profile.roomsToKeep;

        if (profile.overrideSeed)
            seed = profile.seed;

        useSelectedRunSeed = profile.useSelectedRunSeed;
        randomizeSeedWhenNoRunSelected = profile.randomizeSeedWhenNoRunSelected;

        spawnTimeCamperAfterStartingRooms =
            profile.spawnTimeCamperAfterStartingRooms;

        profile.ApplyProgressionTo(progression);

        useGridOccupancy = profile.useGridOccupancy;
        useBoundsOverlapCheck = profile.useBoundsOverlapCheck;
        roomBoundsInset = profile.roomBoundsInset;
        placementAttempts = profile.placementAttempts;

        validateConnectedDoorwayClearance =
            profile.validateConnectedDoorwayClearance;
        doorwayClearanceWidth = profile.doorwayClearanceWidth;
        doorwayClearanceHeight = profile.doorwayClearanceHeight;
        doorwayClearanceDepth = profile.doorwayClearanceDepth;
        ignoreFloorLevelDoorwayBlockers =
            profile.ignoreFloorLevelDoorwayBlockers;
        doorwayFloorBlockerTolerance = profile.doorwayFloorBlockerTolerance;
        doorwayIgnoredLayers = profile.doorwayIgnoredLayers;
        doorwayBlockingLayers = profile.doorwayBlockingLayers;

        createNavMeshLinksBetweenRooms = profile.createNavMeshLinksBetweenRooms;
        navMeshLinkWidth = profile.navMeshLinkWidth;
        navMeshLinkWorldHeight = profile.navMeshLinkWorldHeight;
        navMeshLinkHalfLength = profile.navMeshLinkHalfLength;

        logGenerationReport = profile.logGenerationReport;
        logRejectedFullMapAttempts = profile.logRejectedFullMapAttempts;
        renameGeneratedRoomsForDebug = profile.renameGeneratedRoomsForDebug;
        drawGeneratedMapGizmos = profile.drawGeneratedMapGizmos;
        drawGeneratedMapLabels = profile.drawGeneratedMapLabels;
        drawClosedConnectorGizmos = profile.drawClosedConnectorGizmos;
        drawBranchConnectionGizmos = profile.drawBranchConnectionGizmos;
        generationDebugMarkerSize = profile.generationDebugMarkerSize;
        generationDebugLabelHeight = profile.generationDebugLabelHeight;
    }

    void Start()
    {
        if (IsMultiplayerRun())
        {
            initialGenerationCoroutine = StartCoroutine(
                GenerateInitialRoomsWhenNetworkReady());
            return;
        }

        GenerateInitialRooms();
    }

    IEnumerator GenerateInitialRoomsWhenNetworkReady()
    {
        while (isActiveAndEnabled)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.IsListening)
            {
                initialGenerationCoroutine = null;
                GenerateInitialRooms();
                yield break;
            }

            yield return null;
        }

        initialGenerationCoroutine = null;
    }

    void GenerateInitialRooms()
    {
        if (!CanGenerateRooms())
        {
            Debug.Log("RoomGenerator skipped procedural generation because this instance is a multiplayer client.");
            return;
        }

        ResolveRunSeed();
        Random.InitState(seed);

        if (generateFullMapOnStart)
        {
            GenerateFullMap();
            return;
        }

        int roomsToGenerate = Mathf.Max(1, startingRoomCount);
        for (int i = 0; i < roomsToGenerate; i++)
            GenerateNextRoom();

        TrySpawnWaterValve();
        NotifyGeneratedMapSnapshotReady();
    }

    void StopInitialGenerationCoroutine()
    {
        if (initialGenerationCoroutine == null)
            return;

        StopCoroutine(initialGenerationCoroutine);
        initialGenerationCoroutine = null;
    }

    void ResolveRunSeed()
    {
        if (useSelectedRunSeed && RegionRunState.HasSelectedRegion)
        {
            seed = RegionRunState.RunSeed;
            Debug.Log($"RoomGenerator using {RegionRunState.RegionName} run seed {seed}.");
            return;
        }

        if (randomizeSeedWhenNoRunSelected)
            seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
    }

    public void GenerateNextRoomFromDoor(DoorTrigger trigger)
    {
        if (!CanGenerateRooms())
        {
            Debug.Log("RoomGenerator ignored room generation request because this instance is a multiplayer client.");
            return;
        }

        GenerateNextRoom(trigger);
        CullOldRooms();
        NotifyGeneratedMapSnapshotReady();
    }

    public GameObject GenerateNextRoom()
    {
        if (!CanGenerateRooms())
        {
            Debug.Log("RoomGenerator ignored room generation request because this instance is a multiplayer client.");
            return null;
        }

        return GenerateNextRoom(null);
    }

    bool CanGenerateRooms()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (IsMultiplayerRun())
        {
            return networkManager != null &&
                networkManager.IsListening &&
                networkManager.IsServer;
        }

        if (networkManager != null && networkManager.IsListening)
            return networkManager.IsServer;

        return true;
    }

    static bool IsMultiplayerRun()
    {
        return RegionRunState.HasSelectedRegion && RegionRunState.IsMultiplayer;
    }

    GameObject GenerateNextRoom(DoorTrigger trigger)
    {
        if (mapConsolidated)
        {
            RecordGenerationRejection("Map is already consolidated.");
            return null;
        }

        if (roomPrefabs == null || roomPrefabs.Length == 0)
        {
            RecordGenerationRejection("No room prefabs assigned.");
            Debug.LogWarning("RoomGenerator has no room prefabs assigned.");
            return null;
        }

        if (!isGeneratingFullMap &&
            maxGeneratedRooms > 0 &&
            generatedRoomCount >= maxGeneratedRooms)
        {
            ConsolidateGeneratedMap();
            return null;
        }

        RoomConnector expansionConnector = generatedRoomCount > 0
            ? GetExpansionConnector(trigger)
            : null;

        Transform expansionPoint = expansionConnector != null
            ? expansionConnector.Point
            : (openConnectors.Count == 0 ? lastExitPoint : null);

        if (generatedRoomCount > 0 && expansionPoint == null)
        {
            RecordGenerationRejection("No open connector available to expand from.");
            Debug.LogWarning("RoomGenerator has no open connector available to expand from.");
            return null;
        }

        Vector2Int targetCell;
        if (!TryGetRoomTargetCell(expansionConnector, out targetCell))
        {
            RecordGenerationRejection("Could not calculate target cell for selected connector.");
            Debug.LogWarning("RoomGenerator could not calculate a target cell for the selected connector.");
            CloseBlockedConnector(expansionConnector);
            return null;
        }

        if (IsGridCellOccupied(targetCell))
        {
            RecordGenerationRejection($"Selected connector target cell {targetCell} is occupied.");
            Debug.LogWarning($"RoomGenerator blocked generation at occupied cell {targetCell}.");
            CloseBlockedConnector(expansionConnector);
            return null;
        }

        List<GameObject> rejectedPrefabs = new List<GameObject>();
        int attempts = Mathf.Max(1, placementAttempts);
        bool blockedByPlacement = false;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            GameObject roomPrefab = ChooseRoomPrefab(expansionConnector, rejectedPrefabs);
            if (roomPrefab == null)
            {
                RecordGenerationRejection("No available room prefab for current rules.");
                Debug.LogWarning("RoomGenerator has no available room prefab for the current rules.");
                break;
            }

            GameObject room = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity);

            if (generatedRoomCount > 0 && !AlignRoomToExpansion(room, expansionConnector, expansionPoint))
            {
                RecordGenerationRejection($"{roomPrefab.name} could not align to selected connector.");
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                continue;
            }

            if (generatedRoomCount > 0 &&
                !HasClearConnectedDoorway(room, expansionConnector))
            {
                RecordGenerationRejection($"{roomPrefab.name} connected doorway is blocked.");
                Debug.LogWarning(
                    $"RoomGenerator rejected {room.name}: connected doorway is blocked by room geometry.");
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                continue;
            }

            RoomPlacement placement;
            string rejectionReason;
            if (!CanPlaceRoom(room, targetCell, out placement, out rejectionReason))
            {
                RecordGenerationRejection(rejectionReason);
                Debug.LogWarning($"RoomGenerator rejected {room.name}: {rejectionReason}");
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                blockedByPlacement = true;
                continue;
            }

            if (generatedRoomCount > 0 && !FinalizeRoomConnection(room, expansionConnector))
            {
                RecordGenerationRejection($"{roomPrefab.name} could not finalize connector link.");
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                continue;
            }

            CompleteRoomGeneration(room, roomPrefab, placement);

            if (ShouldConsolidateAfterRoom(room))
                ConsolidateGeneratedMap();

            return room;
        }

        if (blockedByPlacement)
            CloseBlockedConnector(expansionConnector);

        return null;
    }

    void CompleteRoomGeneration(GameObject room, GameObject roomPrefab, RoomPlacement placement)
    {
        int roomIndex = generatedRoomCount;
        spawnedRooms.Add(room);
        RegisterRoomPlacement(placement);
        generatedPrefabIndicesByRoom[room] = GetRoomPrefabIndex(roomPrefab);
        TrackGeneratedPrefab(roomPrefab, roomIndex);
        generatedRoomCount++;
        RecordGeneratedRoom(room, roomPrefab, placement, roomIndex);

        RegisterRoomDoors(room);
        RegisterRoomNavMesh(room);
        SpawnOrQueueRoomContent(room, roomIndex);

        AddOpenConnectors(room);
        UpdateLegacyExitPoint(room);

        if (!isGeneratingFullMap)
            TrySpawnInitialTimeCamper();
    }

    void GenerateFullMap()
    {
        if (roomPrefabs == null || roomPrefabs.Length == 0)
        {
            Debug.LogWarning("RoomGenerator has no room prefabs assigned.");
            return;
        }

        lastGenerationReport = string.Empty;
        lastCompletedGenerationReport = null;

        int baseSeed = seed;
        int attempts = validateFullMapAfterGeneration
            ? Mathf.Max(1, fullMapGenerationAttempts)
            : 1;
        MapValidationResult lastValidation = null;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0)
                ClearGeneratedMapForRetry();

            seed = GetFullMapAttemptSeed(baseSeed, attempt);
            Random.InitState(seed);
            BeginFullMapGenerationReport(attempt + 1, attempts, seed);

            FullMapGenerationStats stats;
            isGeneratingFullMap = true;
            try
            {
                stats = GenerateFullMapAttempt();
            }
            finally
            {
                isGeneratingFullMap = false;
            }

            ConsolidateGeneratedMap();
            lastValidation = ValidateGeneratedMap(stats);
            bool accepted = !validateFullMapAfterGeneration || lastValidation.IsValid;
            FinishFullMapGenerationReport(stats, lastValidation, accepted);

            if (accepted)
            {
                if (attempt > 0)
                {
                    Debug.Log(
                        $"RoomGenerator accepted full map attempt {attempt + 1}/{attempts} with seed {seed}.");
                }

                if (logGenerationReport && !string.IsNullOrWhiteSpace(lastGenerationReport))
                    Debug.Log(lastGenerationReport);

                TrySpawnWaterValve();
                TrySpawnInitialTimeCamper();
                NotifyGeneratedMapSnapshotReady();
                return;
            }

            if (logRejectedFullMapAttempts)
            {
                Debug.LogWarning(
                    $"RoomGenerator rejected full map attempt {attempt + 1}/{attempts} with seed {seed}: {lastValidation.GetSummary()}");
            }
        }

        Debug.LogError(
            $"RoomGenerator could not produce a valid full map after {attempts} attempt(s). Keeping last generated map for inspection. Last validation: {(lastValidation != null ? lastValidation.GetSummary() : "no validation result")}");

        if (logGenerationReport && !string.IsNullOrWhiteSpace(lastGenerationReport))
            Debug.Log(lastGenerationReport);

        NotifyGeneratedMapSnapshotReady();
    }

    FullMapGenerationStats GenerateFullMapAttempt()
    {
        FullMapGenerationStats stats = new FullMapGenerationStats
        {
            requiredBranchCount = Mathf.Max(1, minimumBranchCount)
        };

        GameObject startRoom = GenerateNextRoom();
        if (startRoom == null)
            return stats;

        RecordStartRoom(startRoom);

        stats.requestedBranchCount = Mathf.Max(
            stats.requiredBranchCount,
            GetRandomConfiguredValue(
                minimumBranchCount,
                maximumBranchCount));
        RecordFullMapGenerationPlan(stats);

        int maxBranchBuildAttempts = Mathf.Max(
            Mathf.Max(stats.requestedBranchCount, stats.requiredBranchCount) * 2,
            Mathf.Max(1, minimumRoomsPerBranch) * stats.requiredBranchCount);

        while (stats.completedBranchCount < stats.requestedBranchCount &&
            stats.branchBuildAttempts < maxBranchBuildAttempts)
        {
            stats.branchBuildAttempts++;

            RoomConnector branchStart = ChooseBestOpenConnector();
            if (branchStart == null)
            {
                RecordGenerationRejection("No open connector available for next branch.");
                Debug.LogWarning(
                    $"RoomGenerator stopped full map generation after {stats.completedBranchCount} branches because no open connector was available.");
                break;
            }

            int branchRoomCount = GetRandomConfiguredValue(
                minimumRoomsPerBranch,
                maximumRoomsPerBranch);
            int futureBranchStartsNeeded = guaranteeMinimumBranchCount
                ? Mathf.Max(
                    0,
                    stats.requiredBranchCount - stats.completedBranchCount - 1)
                : 0;

            BeginBranchGenerationReport(
                stats.branchBuildAttempts,
                branchStart,
                branchRoomCount,
                futureBranchStartsNeeded);

            bool branchCompleted = GenerateBranch(
                branchStart,
                branchRoomCount,
                futureBranchStartsNeeded);
            FinishBranchGenerationReport(
                branchCompleted,
                branchCompleted ? null : "Branch could not reach a final room.");

            if (branchCompleted)
            {
                stats.completedBranchCount++;
            }
        }

        Debug.Log(
            $"RoomGenerator generated full map attempt with {generatedRoomCount} rooms and {stats.completedBranchCount}/{stats.requestedBranchCount} completed branches.");

        return stats;
    }

    int GetFullMapAttemptSeed(int baseSeed, int attemptIndex)
    {
        unchecked
        {
            return baseSeed + attemptIndex * 73856093;
        }
    }

    void BeginFullMapGenerationReport(int attemptNumber, int totalAttempts, int attemptSeed)
    {
        currentGenerationReport = new FullMapGenerationReport
        {
            attemptNumber = attemptNumber,
            totalAttempts = totalAttempts,
            seed = attemptSeed
        };
        currentBranchReport = null;
    }

    void RecordFullMapGenerationPlan(FullMapGenerationStats stats)
    {
        if (currentGenerationReport == null || stats == null)
            return;

        currentGenerationReport.requiredBranchCount = stats.requiredBranchCount;
        currentGenerationReport.requestedBranchCount = stats.requestedBranchCount;
    }

    void RecordStartRoom(GameObject room)
    {
        if (currentGenerationReport == null || room == null)
            return;

        if (string.IsNullOrWhiteSpace(currentGenerationReport.startRoomName))
            currentGenerationReport.startRoomName = room.name;
    }

    void BeginBranchGenerationReport(
        int branchNumber,
        RoomConnector branchStart,
        int requestedRoomCount,
        int futureBranchStartsNeeded)
    {
        if (currentGenerationReport == null)
            return;

        currentBranchReport = new BranchGenerationReport
        {
            branchNumber = branchNumber,
            requestedRoomCount = requestedRoomCount,
            futureBranchStartsNeeded = futureBranchStartsNeeded,
            startConnectorName = GetConnectorDebugName(branchStart)
        };

        currentGenerationReport.branches.Add(currentBranchReport);
    }

    void FinishBranchGenerationReport(bool completed, string failureReason)
    {
        if (currentBranchReport == null)
            return;

        currentBranchReport.completed = completed;
        if (!completed)
        {
            currentBranchReport.failureReason =
                string.IsNullOrWhiteSpace(failureReason)
                    ? "Branch generation failed."
                    : failureReason;
            RecordGenerationRejection(currentBranchReport.failureReason);
        }

        currentBranchReport = null;
    }

    void RecordGeneratedRoom(
        GameObject room,
        GameObject roomPrefab,
        RoomPlacement placement,
        int roomIndex)
    {
        if (room == null)
            return;

        RecordRoomDebugInfo(room, placement, roomIndex);

        if (currentGenerationReport == null)
            return;

        if (renameGeneratedRoomsForDebug)
            room.name = GetGeneratedRoomDebugName(roomPrefab, placement, roomIndex);

        string roomLine = GetGeneratedRoomReportLine(room, placement);

        if (currentBranchReport == null)
        {
            currentGenerationReport.startRoomName = roomLine;
            return;
        }

        currentBranchReport.generatedRoomCount++;
        currentBranchReport.rooms.Add(roomLine);

        if (IsFinalRoom(room))
            currentBranchReport.finalRoomName = room.name;
    }

    void RecordRoomDebugInfo(
        GameObject room,
        RoomPlacement placement,
        int roomIndex)
    {
        if (room == null)
            return;

        int branchNumber = currentBranchReport != null
            ? currentBranchReport.branchNumber
            : 0;
        int branchRoomNumber = currentBranchReport != null
            ? currentBranchReport.generatedRoomCount + 1
            : roomIndex;

        roomDebugInfoByRoom[room] = new RoomDebugInfo
        {
            roomIndex = roomIndex,
            branchNumber = branchNumber,
            branchRoomNumber = branchRoomNumber,
            isStart = roomIndex == 0,
            isFinal = IsFinalRoom(room),
            cell = placement != null ? placement.cell : Vector2Int.zero
        };
    }

    void FinishFullMapGenerationReport(
        FullMapGenerationStats stats,
        MapValidationResult validation,
        bool accepted)
    {
        if (currentGenerationReport == null)
            return;

        currentGenerationReport.accepted = accepted;
        currentGenerationReport.validationSummary =
            validation != null ? validation.GetSummary() : "no validation result";

        if (stats != null)
        {
            currentGenerationReport.requiredBranchCount = stats.requiredBranchCount;
            currentGenerationReport.requestedBranchCount = stats.requestedBranchCount;
            currentGenerationReport.completedBranchCount = stats.completedBranchCount;
            currentGenerationReport.branchBuildAttempts = stats.branchBuildAttempts;
        }

        currentGenerationReport.generatedRoomCount = spawnedRooms.Count;
        currentGenerationReport.finalRoomCount = CountFinalRooms();
        currentGenerationReport.closedConnectorCount = CountClosedConnectors();
        currentGenerationReport.connectedConnectorPairCount = CountConnectedConnectorPairs();
        currentGenerationReport.navMeshLinkCount = CountGeneratedNavMeshLinks();

        lastCompletedGenerationReport = currentGenerationReport;
        lastGenerationReport = BuildFullMapGenerationReport(currentGenerationReport);
        currentGenerationReport = null;
        currentBranchReport = null;
    }

    void RecordGenerationRejection(string reason)
    {
        if (currentGenerationReport == null || string.IsNullOrWhiteSpace(reason))
            return;

        int count;
        currentGenerationReport.rejectionCounts.TryGetValue(reason, out count);
        currentGenerationReport.rejectionCounts[reason] = count + 1;
    }

    void RecordAdjacentBranchConnections(int count)
    {
        if (currentGenerationReport == null || count <= 0)
            return;

        currentGenerationReport.adjacentBranchConnectionCount += count;
    }

    string BuildFullMapGenerationReport(FullMapGenerationReport report)
    {
        if (report == null)
            return string.Empty;

        StringBuilder builder = new StringBuilder(2048);
        builder.AppendLine("RoomGenerator Full Map Report");
        builder.AppendLine($"Status: {(report.accepted ? "Accepted" : "Rejected")}");
        builder.AppendLine($"Attempt: {report.attemptNumber}/{report.totalAttempts}");
        builder.AppendLine($"Seed: {report.seed}");
        builder.AppendLine(
            $"Branches: {report.completedBranchCount}/{report.requestedBranchCount} completed, {report.requiredBranchCount} required");
        builder.AppendLine($"Branch build attempts: {report.branchBuildAttempts}");
        builder.AppendLine($"Branch connections: {report.adjacentBranchConnectionCount}");
        builder.AppendLine(
            $"Rooms: {report.generatedRoomCount} total, {report.finalRoomCount} final");
        builder.AppendLine(
            $"Connectors: {report.connectedConnectorPairCount} connected pair(s), {report.closedConnectorCount} closed");
        builder.AppendLine($"NavMeshLinks: {report.navMeshLinkCount}");
        builder.AppendLine($"Validation: {report.validationSummary}");

        if (!string.IsNullOrWhiteSpace(report.startRoomName))
            builder.AppendLine($"Start: {report.startRoomName}");

        if (report.branches.Count > 0)
        {
            builder.AppendLine("Branches:");
            for (int i = 0; i < report.branches.Count; i++)
                AppendBranchReport(builder, report.branches[i]);
        }

        if (report.rejectionCounts.Count > 0)
        {
            builder.AppendLine("Rejections:");
            foreach (KeyValuePair<string, int> pair in report.rejectionCounts)
                builder.AppendLine($"- {pair.Value}x {pair.Key}");
        }

        return builder.ToString();
    }

    void AppendBranchReport(StringBuilder builder, BranchGenerationReport branch)
    {
        if (branch == null)
            return;

        string status = branch.completed ? "OK" : "FAILED";
        builder.AppendLine(
            $"- Branch {branch.branchNumber:00}: {status}, rooms {branch.generatedRoomCount}/{branch.requestedRoomCount}, start {branch.startConnectorName}");

        if (branch.futureBranchStartsNeeded > 0)
            builder.AppendLine($"  future branch starts needed: {branch.futureBranchStartsNeeded}");

        if (!string.IsNullOrWhiteSpace(branch.finalRoomName))
            builder.AppendLine($"  final: {branch.finalRoomName}");

        if (!string.IsNullOrWhiteSpace(branch.failureReason))
            builder.AppendLine($"  reason: {branch.failureReason}");

        for (int i = 0; i < branch.rooms.Count; i++)
            builder.AppendLine($"  - {branch.rooms[i]}");
    }

    string GetGeneratedRoomDebugName(
        GameObject roomPrefab,
        RoomPlacement placement,
        int roomIndex)
    {
        string prefabName = roomPrefab != null ? roomPrefab.name : "Room";
        string cellName = placement != null
            ? $"Cell_{placement.cell.x}_{placement.cell.y}"
            : "Cell_unknown";

        if (currentBranchReport != null)
        {
            int branchRoomNumber = currentBranchReport.generatedRoomCount + 1;
            return
                $"Branch_{currentBranchReport.branchNumber:00}_Room_{branchRoomNumber:00}_{prefabName}_{cellName}";
        }

        return $"Start_Room_{roomIndex:00}_{prefabName}_{cellName}";
    }

    string GetGeneratedRoomReportLine(GameObject room, RoomPlacement placement)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        string category = definition != null ? definition.category.ToString() : "Unknown";
        string cell = placement != null ? placement.cell.ToString() : "unknown cell";
        return $"{room.name} [{category}] cell {cell}";
    }

    string GetConnectorDebugName(RoomConnector connector)
    {
        if (connector == null)
            return "none";

        RoomDefinition definition = connector.GetComponentInParent<RoomDefinition>();
        string roomName = definition != null ? definition.gameObject.name : connector.gameObject.name;
        return $"{roomName}/{connector.name}";
    }

    int CountClosedConnectors()
    {
        int count = 0;
        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            RoomDefinition definition = GetRoomDefinition(spawnedRooms[i]);
            if (definition == null || definition.connectors == null)
                continue;

            for (int j = 0; j < definition.connectors.Length; j++)
            {
                RoomConnector connector = definition.connectors[j];
                if (connector != null && connector.State == RoomConnectorState.Closed)
                    count++;
            }
        }

        return count;
    }

    int CountConnectedConnectorPairs()
    {
        int count = 0;
        List<RoomConnector> checkedConnectors = new List<RoomConnector>();

        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            RoomDefinition definition = GetRoomDefinition(spawnedRooms[i]);
            if (definition == null || definition.connectors == null)
                continue;

            for (int j = 0; j < definition.connectors.Length; j++)
            {
                RoomConnector connector = definition.connectors[j];
                if (connector == null ||
                    connector.State != RoomConnectorState.Connected ||
                    checkedConnectors.Contains(connector))
                {
                    continue;
                }

                checkedConnectors.Add(connector);
                if (connector.ConnectedTo != null)
                    checkedConnectors.Add(connector.ConnectedTo);

                count++;
            }
        }

        return count;
    }

    int CountGeneratedNavMeshLinks()
    {
        int count = 0;
        List<NavMeshLink> checkedLinks = new List<NavMeshLink>();

        foreach (KeyValuePair<RoomConnector, NavMeshLink> pair in navMeshLinksByConnector)
        {
            NavMeshLink link = pair.Value;
            if (link == null || checkedLinks.Contains(link))
                continue;

            checkedLinks.Add(link);
            count++;
        }

        return count;
    }

    MapValidationResult ValidateGeneratedMap(FullMapGenerationStats stats)
    {
        MapValidationResult result = new MapValidationResult();

        if (stats == null)
        {
            result.Fail("generation stats were not produced");
            return result;
        }

        if (spawnedRooms.Count == 0)
            result.Fail("no rooms were generated");

        if (generatedRoomCount != spawnedRooms.Count)
        {
            result.Fail(
                $"generated room count mismatch: counter={generatedRoomCount}, list={spawnedRooms.Count}");
        }

        if (!mapConsolidated)
            result.Fail("map was not consolidated");

        if (stats.completedBranchCount < stats.requiredBranchCount)
        {
            result.Fail(
                $"completed {stats.completedBranchCount}/{stats.requiredBranchCount} required branches");
        }

        int finalRoomCount = CountFinalRooms();
        if (finalRoomCount < stats.requiredBranchCount)
        {
            result.Fail(
                $"only {finalRoomCount} final room(s) for {stats.requiredBranchCount} required branch(es)");
        }

        if (openConnectors.Count > 0)
            result.Fail($"{openConnectors.Count} connector(s) remained in the open connector list");

        ValidateRequiredRooms(result);
        ValidateRoomPlacements(result);
        ValidateConnectorStates(result);
        ValidateFinalRoomDistances(result);

        return result;
    }

    void ValidateRequiredRooms(MapValidationResult result)
    {
        if (!requirePoolRoomsInFullMap)
            return;

        int requiredPools = Mathf.Max(0, minimumRequiredPoolRooms);
        if (requiredPools <= 0)
            return;

        int poolRoomCount = CountRoomsInCategory(RoomCategory.Pool);
        if (poolRoomCount < requiredPools)
        {
            result.Fail(
                $"only {poolRoomCount} pool room(s) for {requiredPools} required pool room(s)");
        }
    }

    int CountRoomsInCategory(RoomCategory category)
    {
        int count = 0;
        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            RoomDefinition definition = GetRoomDefinition(spawnedRooms[i]);
            if (definition != null && definition.category == category)
                count++;
        }

        return count;
    }

    int CountFinalRooms()
    {
        int count = 0;
        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            if (IsFinalRoom(spawnedRooms[i]))
                count++;
        }

        return count;
    }

    void ValidateFinalRoomDistances(MapValidationResult result)
    {
        if (!enforceFinalRoomMinimumDistance)
            return;
        if (spawnedRooms.Count == 0 || spawnedRooms[0] == null)
            return;

        int minimumDistance = Mathf.Max(1, minimumRoomsPerBranch);
        GameObject startRoom = spawnedRooms[0];

        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            GameObject room = spawnedRooms[i];
            if (room == null || !IsFinalRoom(room))
                continue;

            int distance = GetShortestRoomGraphDistance(startRoom, room);
            if (distance < 0)
            {
                result.Fail($"{room.name} is Final but unreachable from the starting room");
                continue;
            }

            if (distance < minimumDistance)
            {
                result.Fail(
                    $"{room.name} is Final at distance {distance}, minimum required is {minimumDistance}");
            }
        }
    }

    int GetShortestRoomGraphDistance(GameObject startRoom, GameObject targetRoom)
    {
        if (startRoom == null || targetRoom == null)
            return -1;
        if (startRoom == targetRoom)
            return 0;

        Queue<GameObject> queue = new Queue<GameObject>();
        Dictionary<GameObject, int> distances = new Dictionary<GameObject, int>();

        queue.Enqueue(startRoom);
        distances[startRoom] = 0;

        while (queue.Count > 0)
        {
            GameObject currentRoom = queue.Dequeue();
            int currentDistance = distances[currentRoom];

            RoomDefinition definition = GetRoomDefinition(currentRoom);
            if (definition == null || definition.connectors == null)
                continue;

            for (int i = 0; i < definition.connectors.Length; i++)
            {
                RoomConnector connector = definition.connectors[i];
                GameObject connectedRoom = GetConnectedRoom(connector);
                if (connectedRoom == null || distances.ContainsKey(connectedRoom))
                    continue;

                int connectedDistance = currentDistance + 1;
                if (connectedRoom == targetRoom)
                    return connectedDistance;

                distances[connectedRoom] = connectedDistance;
                queue.Enqueue(connectedRoom);
            }
        }

        return -1;
    }

    GameObject GetConnectedRoom(RoomConnector connector)
    {
        if (connector == null || connector.State != RoomConnectorState.Connected)
            return null;

        RoomConnector connectedTo = connector.ConnectedTo;
        if (connectedTo == null)
            return null;

        RoomDefinition definition = connectedTo.GetComponentInParent<RoomDefinition>();
        return definition != null ? definition.gameObject : null;
    }
    void ValidateRoomPlacements(MapValidationResult result)
    {
        List<RoomPlacement> placements = new List<RoomPlacement>();

        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            GameObject room = spawnedRooms[i];
            if (room == null)
            {
                result.Fail($"room index {i} is null");
                continue;
            }

            RoomDefinition definition = GetRoomDefinition(room);
            if (definition == null)
                result.Fail($"{room.name} has no RoomDefinition");

            RoomPlacement placement;
            if (!placementsByRoom.TryGetValue(room, out placement) ||
                placement == null)
            {
                result.Fail($"{room.name} has no registered placement");
                continue;
            }

            placements.Add(placement);

            if (useGridOccupancy)
            {
                RoomPlacement cellPlacement;
                if (!placementsByCell.TryGetValue(placement.cell, out cellPlacement) ||
                    cellPlacement != placement)
                {
                    result.Fail($"{room.name} is not registered in its grid cell {placement.cell}");
                }
            }
        }

        for (int i = 0; i < placements.Count; i++)
        {
            RoomPlacement first = placements[i];
            Bounds firstBounds = ShrinkBounds(first.bounds);

            for (int j = i + 1; j < placements.Count; j++)
            {
                RoomPlacement second = placements[j];
                Bounds secondBounds = ShrinkBounds(second.bounds);

                if (!firstBounds.Intersects(secondBounds))
                    continue;

                string firstName = first.room != null ? first.room.name : "missing room";
                string secondName = second.room != null ? second.room.name : "missing room";
                result.Fail($"{firstName} overlaps {secondName}");
            }
        }
    }

    void ValidateConnectorStates(MapValidationResult result)
    {
        List<RoomConnector> checkedNavMeshConnectors = new List<RoomConnector>();

        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            GameObject room = spawnedRooms[i];
            RoomDefinition definition = GetRoomDefinition(room);
            if (definition == null || definition.connectors == null)
                continue;

            if (definition.category == RoomCategory.Final &&
                HasExitConnector(definition))
            {
                result.Fail($"{room.name} is Final but still has exit connector rules");
            }

            for (int j = 0; j < definition.connectors.Length; j++)
            {
                RoomConnector connector = definition.connectors[j];
                if (connector == null)
                    continue;

                if (connector.IsAvailable)
                    result.Fail($"{room.name}/{connector.name} is still open after consolidation");

                ValidateConnectedConnector(
                    result,
                    room,
                    connector,
                    checkedNavMeshConnectors);

                if (validateClosedDoorObjects)
                    ValidateClosedDoorState(result, room, connector);
            }
        }
    }

    void ValidateConnectedConnector(
        MapValidationResult result,
        GameObject room,
        RoomConnector connector,
        List<RoomConnector> checkedNavMeshConnectors)
    {
        if (connector.State != RoomConnectorState.Connected)
            return;

        RoomConnector connectedTo = connector.ConnectedTo;
        if (connectedTo == null)
        {
            result.Fail($"{room.name}/{connector.name} is connected without a target connector");
            return;
        }

        if (connectedTo.ConnectedTo != connector)
            result.Fail($"{room.name}/{connector.name} has a one-way connector link");

        if (!validateConnectedNavMeshLinks || !createNavMeshLinksBetweenRooms)
            return;

        if (checkedNavMeshConnectors.Contains(connector) ||
            checkedNavMeshConnectors.Contains(connectedTo))
        {
            return;
        }

        checkedNavMeshConnectors.Add(connector);
        checkedNavMeshConnectors.Add(connectedTo);

        NavMeshLink link;
        if (!navMeshLinksByConnector.TryGetValue(connector, out link) ||
            link == null)
        {
            result.Fail($"{room.name}/{connector.name} has no generated NavMeshLink");
            return;
        }

        if (!link.activated)
            result.Fail($"{room.name}/{connector.name} has a disabled NavMeshLink");
    }

    void ValidateClosedDoorState(
        MapValidationResult result,
        GameObject room,
        RoomConnector connector)
    {
        Transform closedDoor = connector.transform.Find(ClosedDoorChildName);

        if (connector.State == RoomConnectorState.Closed)
        {
            if (closedDoor == null)
            {
                result.Fail($"{room.name}/{connector.name} is closed but has no {ClosedDoorChildName} child");
                return;
            }

            if (!closedDoor.gameObject.activeInHierarchy)
            {
                result.Fail($"{room.name}/{connector.name} is closed but {ClosedDoorChildName} is inactive");
                return;
            }

            if (!HasActiveSolidCollider(closedDoor))
            {
                result.Fail($"{room.name}/{connector.name} {ClosedDoorChildName} has no enabled solid collider");
            }

            return;
        }

        if (closedDoor != null && closedDoor.gameObject.activeInHierarchy)
        {
            result.Fail(
                $"{room.name}/{connector.name} is {connector.State} but {ClosedDoorChildName} is active");
        }
    }

    bool HasActiveSolidCollider(Transform root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != null &&
                collider.enabled &&
                !collider.isTrigger &&
                collider.gameObject.activeInHierarchy)
            {
                return true;
            }
        }

        return false;
    }

    void ClearGeneratedMapForRetry()
    {
        StopAllCoroutines();

        if (EnemySpawner.Instance != null)
        {
            for (int i = 0; i < registeredSurfaces.Count; i++)
            {
                NavMeshSurface surface = registeredSurfaces[i];
                if (surface != null)
                    EnemySpawner.Instance.UnregisterSurface(surface);
            }
        }

        registeredSurfaces.Clear();

        if (resourceSpawner != null)
        {
            for (int i = 0; i < spawnedRooms.Count; i++)
                resourceSpawner.DespawnResourcesForRoom(spawnedRooms[i]);
        }

        if (enemySpawner != null)
        {
            for (int i = 0; i < spawnedRooms.Count; i++)
                enemySpawner.DespawnEnemiesForRoom(spawnedRooms[i]);
        }

        if (navMeshLinkRoot != null)
        {
            DestroyGeneratedObject(navMeshLinkRoot.gameObject);
            navMeshLinkRoot = null;
        }

        if (spawnedWaterValve != null)
        {
            DestroyGeneratedObject(spawnedWaterValve);
            spawnedWaterValve = null;
        }

        for (int i = 0; i < spawnedRooms.Count; i++)
            DestroyGeneratedObject(spawnedRooms[i]);

        spawnedRooms.Clear();
        openConnectors.Clear();
        pendingRoomContentSpawns.Clear();
        contentSpawnedRooms.Clear();
        generatedPrefabCounts.Clear();
        lastGeneratedPrefabIndices.Clear();
        generatedPrefabIndicesByRoom.Clear();
        placementsByRoom.Clear();
        placementsByCell.Clear();
        connectorTargetCells.Clear();
        navMeshLinksByConnector.Clear();
        roomDebugInfoByRoom.Clear();
        debugBranchConnections.Clear();
        lastExitPoint = null;
        generatedRoomCount = 0;
        initialEnemySpawned = false;
        pendingInitialTimeCamperSpawn = false;
        mapConsolidated = false;
        generatedMapSnapshotReady = false;
        mapSyncRegistrationCoroutine = null;
        roomContentFlushCoroutine = null;

        Physics.SyncTransforms();
    }

    void DestroyGeneratedObject(GameObject objectToDestroy)
    {
        if (objectToDestroy == null)
            return;

        objectToDestroy.SetActive(false);

        if (Application.isPlaying)
            Destroy(objectToDestroy);
        else
            DestroyImmediate(objectToDestroy);
    }

    int GetRandomConfiguredValue(int firstValue, int secondValue)
    {
        int minValue = Mathf.Max(1, Mathf.Min(firstValue, secondValue));
        int maxValue = Mathf.Max(minValue, Mathf.Max(firstValue, secondValue));

        return Random.Range(minValue, maxValue + 1);
    }

    RoomGenerationSnapshot CaptureGenerationSnapshot()
    {
        return new RoomGenerationSnapshot
        {
            spawnedRoomCount = spawnedRooms.Count,
            registeredSurfaceCount = registeredSurfaces.Count,
            generatedRoomCount = generatedRoomCount,
            initialEnemySpawned = initialEnemySpawned,
            pendingInitialTimeCamperSpawn = pendingInitialTimeCamperSpawn,
            mapConsolidated = mapConsolidated,
            lastExitPoint = lastExitPoint,
            openConnectors = new List<RoomConnector>(openConnectors),
            pendingRoomContentSpawns = new List<PendingRoomContentSpawn>(pendingRoomContentSpawns),
            contentSpawnedRooms = new HashSet<GameObject>(contentSpawnedRooms),
            generatedPrefabCounts = new Dictionary<GameObject, int>(generatedPrefabCounts),
            lastGeneratedPrefabIndices = new Dictionary<GameObject, int>(lastGeneratedPrefabIndices),
            generatedPrefabIndicesByRoom = new Dictionary<GameObject, int>(generatedPrefabIndicesByRoom),
            placementsByRoom = new Dictionary<GameObject, RoomPlacement>(placementsByRoom),
            placementsByCell = new Dictionary<Vector2Int, RoomPlacement>(placementsByCell),
            connectorTargetCells = new Dictionary<RoomConnector, Vector2Int>(connectorTargetCells),
            navMeshLinksByConnector = new Dictionary<RoomConnector, NavMeshLink>(navMeshLinksByConnector),
            roomDebugInfoByRoom = new Dictionary<GameObject, RoomDebugInfo>(roomDebugInfoByRoom),
            debugBranchConnections = new List<ConnectorDebugConnection>(debugBranchConnections)
        };
    }

    void RestoreGenerationSnapshot(RoomGenerationSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        UnregisterGeneratedSurfacesFromSnapshot(snapshot);
        DestroyGeneratedNavMeshLinksFromSnapshot(snapshot);
        DestroyGeneratedRoomsFromSnapshot(snapshot);

        generatedRoomCount = snapshot.generatedRoomCount;
        initialEnemySpawned = snapshot.initialEnemySpawned;
        pendingInitialTimeCamperSpawn = snapshot.pendingInitialTimeCamperSpawn;
        mapConsolidated = snapshot.mapConsolidated;
        lastExitPoint = snapshot.lastExitPoint;

        RestoreList(openConnectors, snapshot.openConnectors);
        RestoreList(pendingRoomContentSpawns, snapshot.pendingRoomContentSpawns);
        RestoreHashSet(contentSpawnedRooms, snapshot.contentSpawnedRooms);
        RestoreDictionary(generatedPrefabCounts, snapshot.generatedPrefabCounts);
        RestoreDictionary(lastGeneratedPrefabIndices, snapshot.lastGeneratedPrefabIndices);
        RestoreDictionary(generatedPrefabIndicesByRoom, snapshot.generatedPrefabIndicesByRoom);
        RestoreDictionary(placementsByRoom, snapshot.placementsByRoom);
        RestoreDictionary(placementsByCell, snapshot.placementsByCell);
        RestoreDictionary(connectorTargetCells, snapshot.connectorTargetCells);
        RestoreDictionary(navMeshLinksByConnector, snapshot.navMeshLinksByConnector);
        RestoreDictionary(roomDebugInfoByRoom, snapshot.roomDebugInfoByRoom);
        RestoreList(debugBranchConnections, snapshot.debugBranchConnections);

        for (int i = 0; i < openConnectors.Count; i++)
        {
            RoomConnector connector = openConnectors[i];
            if (connector != null && !connector.IsAvailable)
                connector.Open();
        }

        Physics.SyncTransforms();
    }

    void UnregisterGeneratedSurfacesFromSnapshot(RoomGenerationSnapshot snapshot)
    {
        for (int i = registeredSurfaces.Count - 1; i >= snapshot.registeredSurfaceCount; i--)
        {
            NavMeshSurface surface = registeredSurfaces[i];
            if (surface != null && EnemySpawner.Instance != null)
                EnemySpawner.Instance.UnregisterSurface(surface);

            registeredSurfaces.RemoveAt(i);
        }
    }

    void DestroyGeneratedRoomsFromSnapshot(RoomGenerationSnapshot snapshot)
    {
        for (int i = spawnedRooms.Count - 1; i >= snapshot.spawnedRoomCount; i--)
        {
            GameObject room = spawnedRooms[i];
            if (resourceSpawner != null)
                resourceSpawner.DespawnResourcesForRoom(room);

            if (enemySpawner != null)
                enemySpawner.DespawnEnemiesForRoom(room);

            contentSpawnedRooms.Remove(room);
            DestroyGeneratedObject(room);
            spawnedRooms.RemoveAt(i);
        }
    }

    void DestroyGeneratedNavMeshLinksFromSnapshot(RoomGenerationSnapshot snapshot)
    {
        List<NavMeshLink> linksToDestroy = new List<NavMeshLink>();

        foreach (KeyValuePair<RoomConnector, NavMeshLink> pair in navMeshLinksByConnector)
        {
            NavMeshLink link = pair.Value;
            if (link == null || linksToDestroy.Contains(link))
                continue;

            if (snapshot.navMeshLinksByConnector.ContainsValue(link))
                continue;

            linksToDestroy.Add(link);
        }

        for (int i = 0; i < linksToDestroy.Count; i++)
            DestroyGeneratedObject(linksToDestroy[i].gameObject);
    }

    void RestoreList<T>(List<T> target, List<T> snapshot)
    {
        target.Clear();
        if (snapshot == null)
            return;

        target.AddRange(snapshot);
    }

    void RestoreDictionary<TKey, TValue>(
        Dictionary<TKey, TValue> target,
        Dictionary<TKey, TValue> snapshot)
    {
        target.Clear();
        if (snapshot == null)
            return;

        foreach (KeyValuePair<TKey, TValue> pair in snapshot)
            target[pair.Key] = pair.Value;
    }

    void RestoreHashSet<T>(HashSet<T> target, HashSet<T> snapshot)
    {
        target.Clear();
        if (snapshot == null)
            return;

        foreach (T value in snapshot)
            target.Add(value);
    }

    bool GenerateBranch(
        RoomConnector branchStart,
        int roomCount,
        int futureBranchStartsNeeded)
    {
        RoomGenerationSnapshot snapshot = CaptureGenerationSnapshot();
        RoomConnector currentConnector = branchStart;
        int roomsToGenerate = Mathf.Max(1, roomCount);

        for (int i = 0; i < roomsToGenerate; i++)
        {
            bool finalStep = i == roomsToGenerate - 1;
            RoomGenerationRole role = finalStep
                ? RoomGenerationRole.BranchFinal
                : RoomGenerationRole.BranchMiddle;
            int minimumOpenExits = 0;

            if (!finalStep)
            {
                minimumOpenExits = 1;

                if (futureBranchStartsNeeded > 0)
                {
                    int availableFutureStarts =
                        CountViableOpenConnectors(currentConnector);
                    if (availableFutureStarts < futureBranchStartsNeeded)
                        minimumOpenExits = 2;
                }
            }

            RoomConnector nextConnector;
            GameObject generatedRoom = GenerateRoomFromConnector(
                currentConnector,
                role,
                requireContinuation: !finalStep,
                minimumOpenExits: minimumOpenExits,
                out nextConnector);

            if (generatedRoom == null)
            {
                if (spawnedRooms.Count > snapshot.spawnedRoomCount)
                    RestoreGenerationSnapshot(snapshot);
                else
                    CloseBlockedConnector(currentConnector);

                return false;
            }

            currentConnector = nextConnector;
        }

        return true;
    }

    GameObject GenerateRoomFromConnector(
        RoomConnector expansionConnector,
        RoomGenerationRole role,
        bool requireContinuation,
        int minimumOpenExits,
        out RoomConnector continuationConnector)
    {
        continuationConnector = null;

        if (mapConsolidated)
        {
            RecordGenerationRejection("Map is already consolidated.");
            return null;
        }

        if (expansionConnector == null || !expansionConnector.IsAvailable)
        {
            RecordGenerationRejection("Expansion connector is missing or unavailable.");
            return null;
        }

        Transform expansionPoint = expansionConnector.Point;
        if (expansionPoint == null)
        {
            RecordGenerationRejection("Expansion connector has no point.");
            CloseBlockedConnector(expansionConnector);
            return null;
        }

        Vector2Int targetCell;
        if (!TryGetRoomTargetCell(expansionConnector, out targetCell))
        {
            RecordGenerationRejection("Could not calculate target cell.");
            CloseBlockedConnector(expansionConnector);
            return null;
        }

        if (IsGridCellOccupied(targetCell))
        {
            RecordGenerationRejection($"Target cell {targetCell} is already occupied.");
            CloseBlockedConnector(expansionConnector);
            return null;
        }

        List<GameObject> rejectedPrefabs = new List<GameObject>();
        int attempts = Mathf.Max(1, branchGenerationAttempts);
        bool blockedByPlacement = false;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            GameObject roomPrefab = ChooseRoomPrefabForRole(
                expansionConnector,
                rejectedPrefabs,
                role);

            if (roomPrefab == null)
            {
                RecordGenerationRejection($"No compatible prefab for role {role}.");
                break;
            }

            GameObject room = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity);

            if (!AlignRoomToExpansion(room, expansionConnector, expansionPoint))
            {
                RecordGenerationRejection($"{roomPrefab.name} could not align to connector.");
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                continue;
            }

            if (!HasClearConnectedDoorway(room, expansionConnector))
            {
                RecordGenerationRejection($"{roomPrefab.name} connected doorway is blocked.");
                Debug.LogWarning(
                    $"RoomGenerator rejected {room.name}: connected doorway is blocked by room geometry.");
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                continue;
            }

            RoomPlacement placement;
            string rejectionReason;
            if (!CanPlaceRoom(room, targetCell, out placement, out rejectionReason))
            {
                RecordGenerationRejection(rejectionReason);
                Debug.LogWarning($"RoomGenerator rejected {room.name}: {rejectionReason}");
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                blockedByPlacement = true;
                continue;
            }

            RoomConnector entryConnector = GetCompatibleEntranceConnector(
                room,
                expansionConnector);
            RoomConnector selectedContinuation = null;
            int openExitCount = 0;

            if (requireContinuation &&
                !TryGetBestContinuationConnector(
                    room,
                    placement,
                    entryConnector,
                    Mathf.Max(1, minimumOpenExits),
                    out selectedContinuation,
                    out openExitCount))
            {
                RecordGenerationRejection(
                    $"{roomPrefab.name} has only {openExitCount} free exit(s), needs {Mathf.Max(1, minimumOpenExits)}.");
                Debug.LogWarning(
                    $"RoomGenerator rejected {room.name}: only {openExitCount} free exit(s), but {Mathf.Max(1, minimumOpenExits)} are needed to keep the branch plan valid.");
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                continue;
            }

            if (!FinalizeRoomConnection(room, expansionConnector))
            {
                RecordGenerationRejection($"{roomPrefab.name} could not finalize connector link.");
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                continue;
            }

            CompleteRoomGeneration(room, roomPrefab, placement);
            continuationConnector = selectedContinuation;
            return room;
        }

        if (blockedByPlacement)
            CloseBlockedConnector(expansionConnector);

        return null;
    }

    GameObject ChooseRoomPrefab(RoomConnector expansionConnector)
    {
        return ChooseRoomPrefab(expansionConnector, null);
    }

    GameObject ChooseRoomPrefab(RoomConnector expansionConnector, List<GameObject> rejectedPrefabs)
    {
        GameObject prefab = ChooseRoomPrefab(expansionConnector, useProgressionFilter: true, rejectedPrefabs);
        if (prefab != null)
            return prefab;

        if (progression != null && progression.ShouldFallbackWhenNoPrefabMatches)
        {
            Debug.LogWarning(
                $"RoomGenerator found no room for progression rule '{progression.GetRuleLabel(generatedRoomCount, maxGeneratedRooms)}' at index {generatedRoomCount}. Falling back to any compatible category.");
            return ChooseRoomPrefab(expansionConnector, useProgressionFilter: false, rejectedPrefabs);
        }

        return null;
    }

    GameObject ChooseRoomPrefab(
        RoomConnector expansionConnector,
        bool useProgressionFilter,
        List<GameObject> rejectedPrefabs)
    {
        float totalWeight = 0f;

        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            GameObject prefab = roomPrefabs[i];
            if (IsPrefabRejected(prefab, rejectedPrefabs)) continue;
            if (!CanSpawnRoomPrefab(prefab, useProgressionFilter)) continue;
            if (!CanRoomPrefabConnectTo(prefab, expansionConnector)) continue;
            totalWeight += GetRoomPrefabWeight(prefab);
        }

        if (totalWeight <= 0f)
            return null;

        float roll = Random.Range(0f, totalWeight);
        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            GameObject prefab = roomPrefabs[i];
            if (IsPrefabRejected(prefab, rejectedPrefabs)) continue;
            if (!CanSpawnRoomPrefab(prefab, useProgressionFilter)) continue;
            if (!CanRoomPrefabConnectTo(prefab, expansionConnector)) continue;

            roll -= GetRoomPrefabWeight(prefab);
            if (roll <= 0f)
                return prefab;
        }

        return null;
    }

    GameObject ChooseRoomPrefabForRole(
        RoomConnector expansionConnector,
        List<GameObject> rejectedPrefabs,
        RoomGenerationRole role)
    {
        float totalWeight = 0f;

        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            GameObject prefab = roomPrefabs[i];
            if (IsPrefabRejected(prefab, rejectedPrefabs)) continue;
            if (!CanSpawnRoomPrefabForRole(prefab, role)) continue;
            if (!CanRoomPrefabConnectTo(prefab, expansionConnector)) continue;
            totalWeight += GetRoomPrefabWeight(prefab);
        }

        if (totalWeight <= 0f)
            return null;

        float roll = Random.Range(0f, totalWeight);
        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            GameObject prefab = roomPrefabs[i];
            if (IsPrefabRejected(prefab, rejectedPrefabs)) continue;
            if (!CanSpawnRoomPrefabForRole(prefab, role)) continue;
            if (!CanRoomPrefabConnectTo(prefab, expansionConnector)) continue;

            roll -= GetRoomPrefabWeight(prefab);
            if (roll <= 0f)
                return prefab;
        }

        return null;
    }

    bool CanSpawnRoomPrefabForRole(GameObject prefab, RoomGenerationRole role)
    {
        if (role == RoomGenerationRole.Any)
            return CanSpawnRoomPrefab(prefab, useProgressionFilter: true);

        if (prefab == null)
            return false;

        RoomDefinition definition = GetRoomDefinition(prefab);
        if (definition == null)
            return false;

        if (role == RoomGenerationRole.BranchFinal)
            return CanSpawnBranchFinalRoomPrefab(definition);

        if (!definition.CanSpawn(
            GetGeneratedPrefabCount(prefab),
            generatedRoomCount,
            GetRoomsSinceLastInstance(prefab)))
        {
            return false;
        }

        switch (role)
        {
            case RoomGenerationRole.BranchMiddle:
                return definition.category != RoomCategory.Final &&
                    HasExitConnector(definition);

            default:
                return true;
        }
    }

    bool CanSpawnBranchFinalRoomPrefab(RoomDefinition definition)
    {
        if (definition == null || definition.category != RoomCategory.Final)
            return false;

        if (!allowRepeatingFinalRoomsForBranches)
        {
            return definition.CanSpawn(
                GetGeneratedPrefabCount(definition.gameObject),
                generatedRoomCount,
                GetRoomsSinceLastInstance(definition.gameObject));
        }

        return definition.EffectiveSpawnWeight > 0f &&
            generatedRoomCount >= definition.minimumRoomIndex;
    }

    bool HasExitConnector(RoomDefinition definition)
    {
        if (definition == null || definition.connectors == null)
            return false;

        for (int i = 0; i < definition.connectors.Length; i++)
        {
            RoomConnector connector = definition.connectors[i];
            if (connector != null && connector.canBeExit)
                return true;
        }

        return false;
    }

    bool IsPrefabRejected(GameObject prefab, List<GameObject> rejectedPrefabs)
    {
        return prefab != null && rejectedPrefabs != null && rejectedPrefabs.Contains(prefab);
    }

    RoomConnector GetExpansionConnector(DoorTrigger trigger)
    {
        CleanOpenConnectors();

        if (trigger != null)
        {
            RoomConnector triggerConnector = FindOpenConnectorForTrigger(trigger);
            if (triggerConnector == null)
            {
                Debug.LogWarning(
                    $"{trigger.name} requested a new room, but it is not linked to an open RoomConnector.");
            }

            return triggerConnector;
        }

        return ChooseOpenConnector();
    }

    RoomConnector ChooseOpenConnector()
    {
        CleanOpenConnectors();
        if (openConnectors.Count == 0)
            return null;

        return openConnectors[Random.Range(0, openConnectors.Count)];
    }

    RoomConnector ChooseBestOpenConnector()
    {
        CleanOpenConnectors();

        RoomConnector bestConnector = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < openConnectors.Count; i++)
        {
            RoomConnector connector = openConnectors[i];
            if (connector == null || !connector.IsAvailable)
                continue;

            Vector2Int targetCell;
            if (!TryGetConnectorTargetCell(connector, out targetCell))
                continue;

            if (IsGridCellOccupied(targetCell))
                continue;

            int score = CountFreeNeighborCells(targetCell);
            bool beatsCurrent = bestConnector == null || score > bestScore;
            bool breaksTie = score == bestScore && Random.value < 0.5f;

            if (beatsCurrent || breaksTie)
            {
                bestConnector = connector;
                bestScore = score;
            }
        }

        return bestConnector;
    }

    RoomConnector FindOpenConnectorForTrigger(DoorTrigger trigger)
    {
        if (trigger == null) return null;

        for (int i = 0; i < openConnectors.Count; i++)
        {
            RoomConnector connector = openConnectors[i];
            if (connector != null && connector.Trigger == trigger)
                return connector;
        }

        return null;
    }

    bool CanRoomPrefabConnectTo(GameObject prefab, RoomConnector expansionConnector)
    {
        if (prefab == null) return false;
        if (expansionConnector == null) return true;

        RoomDefinition definition = GetRoomDefinition(prefab);
        if (definition != null && definition.connectors != null && definition.connectors.Length > 0)
        {
            RoomConnector connector;
            return definition.TryGetEntranceConnector(expansionConnector, out connector);
        }

        return prefab.transform.Find("DoorPoint_A") != null;
    }

    bool TryGetBestContinuationConnector(
        GameObject room,
        RoomPlacement placement,
        RoomConnector excludedConnector,
        int minimumOpenExitCount,
        out RoomConnector continuationConnector,
        out int openExitCount)
    {
        continuationConnector = null;
        openExitCount = 0;

        RoomDefinition definition = GetRoomDefinition(room);
        if (definition == null || definition.connectors == null || placement == null)
            return false;

        int bestScore = int.MinValue;

        for (int i = 0; i < definition.connectors.Length; i++)
        {
            RoomConnector connector = definition.connectors[i];
            if (connector == null || connector == excludedConnector)
                continue;
            if (!connector.canBeExit || !connector.IsAvailable)
                continue;

            Vector2Int step = GetConnectorStep(connector);
            if (step == Vector2Int.zero)
                continue;

            Vector2Int targetCell = placement.cell + step;
            if (IsGridCellOccupied(targetCell))
                continue;

            openExitCount++;

            int score = CountFreeNeighborCells(targetCell);
            bool beatsCurrent =
                continuationConnector == null ||
                score > bestScore;
            bool breaksTie = score == bestScore && Random.value < 0.5f;

            if (beatsCurrent || breaksTie)
            {
                continuationConnector = connector;
                bestScore = score;
            }
        }

        return continuationConnector != null &&
            openExitCount >= Mathf.Max(1, minimumOpenExitCount);
    }

    int CountViableOpenConnectors(RoomConnector excludedConnector)
    {
        CleanOpenConnectors();

        int count = 0;
        for (int i = 0; i < openConnectors.Count; i++)
        {
            RoomConnector connector = openConnectors[i];
            if (connector == null || connector == excludedConnector)
                continue;
            if (!connector.IsAvailable)
                continue;

            Vector2Int targetCell;
            if (!TryGetConnectorTargetCell(connector, out targetCell))
                continue;

            if (IsGridCellOccupied(targetCell))
                continue;

            count++;
        }

        return count;
    }

    int CountFreeNeighborCells(Vector2Int cell)
    {
        int count = 0;

        if (!IsGridCellOccupied(cell + Vector2Int.up)) count++;
        if (!IsGridCellOccupied(cell + Vector2Int.down)) count++;
        if (!IsGridCellOccupied(cell + Vector2Int.left)) count++;
        if (!IsGridCellOccupied(cell + Vector2Int.right)) count++;

        return count;
    }

    bool CanSpawnRoomPrefab(GameObject prefab, bool useProgressionFilter)
    {
        if (prefab == null) return false;

        RoomDefinition definition = GetRoomDefinition(prefab);
        if (definition == null)
        {
            return progression == null ||
                !useProgressionFilter ||
                progression.AllowsRoom(null, generatedRoomCount, maxGeneratedRooms);
        }

        if (!definition.CanSpawn(
            GetGeneratedPrefabCount(prefab),
            generatedRoomCount,
            GetRoomsSinceLastInstance(prefab)))
        {
            return false;
        }

        return progression == null ||
            !useProgressionFilter ||
            progression.AllowsRoom(definition, generatedRoomCount, maxGeneratedRooms);
    }

    float GetRoomPrefabWeight(GameObject prefab)
    {
        RoomDefinition definition = GetRoomDefinition(prefab);
        return definition != null ? definition.EffectiveSpawnWeight : 1f;
    }

    int GetGeneratedPrefabCount(GameObject prefab)
    {
        int count;
        return prefab != null && generatedPrefabCounts.TryGetValue(prefab, out count) ? count : 0;
    }

    int GetRoomsSinceLastInstance(GameObject prefab)
    {
        int lastIndex;
        if (prefab == null ||
            !lastGeneratedPrefabIndices.TryGetValue(prefab, out lastIndex))
        {
            return int.MaxValue;
        }

        return generatedRoomCount - lastIndex - 1;
    }

    void TrackGeneratedPrefab(GameObject prefab, int roomIndex)
    {
        if (prefab == null) return;

        generatedPrefabCounts[prefab] =
            GetGeneratedPrefabCount(prefab) + 1;
        lastGeneratedPrefabIndices[prefab] = roomIndex;
    }

    bool CanPlaceRoom(
        GameObject room,
        Vector2Int targetCell,
        out RoomPlacement placement,
        out string rejectionReason)
    {
        placement = new RoomPlacement
        {
            room = room,
            cell = targetCell,
            bounds = CalculateRoomBounds(room)
        };
        rejectionReason = null;

        if (IsGridCellOccupied(targetCell))
        {
            rejectionReason = $"cell {targetCell} is already occupied";
            return false;
        }

        GameObject overlappingRoom;
        if (useBoundsOverlapCheck && IntersectsExistingRoom(placement.bounds, out overlappingRoom))
        {
            rejectionReason = overlappingRoom != null
                ? $"bounds overlap {overlappingRoom.name}"
                : "bounds overlap another generated room";
            return false;
        }

        return true;
    }

    void RegisterRoomPlacement(RoomPlacement placement)
    {
        if (placement == null || placement.room == null) return;

        placementsByRoom[placement.room] = placement;
        if (useGridOccupancy)
            placementsByCell[placement.cell] = placement;
    }

    void UnregisterRoomPlacement(GameObject room)
    {
        if (room == null) return;

        RoomPlacement placement;
        if (!placementsByRoom.TryGetValue(room, out placement))
            return;

        placementsByRoom.Remove(room);

        RoomPlacement cellPlacement;
        if (useGridOccupancy &&
            placementsByCell.TryGetValue(placement.cell, out cellPlacement) &&
            cellPlacement == placement)
        {
            placementsByCell.Remove(placement.cell);
        }
    }

    bool TryGetRoomTargetCell(RoomConnector expansionConnector, out Vector2Int targetCell)
    {
        if (generatedRoomCount == 0 || expansionConnector == null)
        {
            targetCell = Vector2Int.zero;
            return true;
        }

        return TryGetConnectorTargetCell(expansionConnector, out targetCell);
    }

    bool TryGetConnectorTargetCell(RoomConnector connector, out Vector2Int targetCell)
    {
        targetCell = Vector2Int.zero;
        if (connector == null)
            return false;

        if (connectorTargetCells.TryGetValue(connector, out targetCell))
            return true;

        RoomPlacement sourcePlacement;
        if (!TryGetConnectorSourcePlacement(connector, out sourcePlacement))
            return false;

        Vector2Int step = GetConnectorStep(connector);
        if (step == Vector2Int.zero)
            return false;

        targetCell = sourcePlacement.cell + step;
        connectorTargetCells[connector] = targetCell;
        return true;
    }

    bool TryGetConnectorSourcePlacement(RoomConnector connector, out RoomPlacement placement)
    {
        placement = null;
        if (connector == null) return false;

        foreach (KeyValuePair<GameObject, RoomPlacement> pair in placementsByRoom)
        {
            GameObject room = pair.Key;
            if (room == null || pair.Value == null) continue;
            if (connector.transform.IsChildOf(room.transform))
            {
                placement = pair.Value;
                return true;
            }
        }

        return false;
    }

    Vector2Int GetConnectorStep(RoomConnector connector)
    {
        if (connector == null)
            return Vector2Int.zero;

        Transform point = connector.Point;
        Vector3 forward = point != null ? point.forward : Vector3.zero;
        forward.y = 0f;

        if (forward.sqrMagnitude > 0.0001f)
        {
            forward.Normalize();
            if (Mathf.Abs(forward.x) > Mathf.Abs(forward.z))
                return new Vector2Int(forward.x >= 0f ? 1 : -1, 0);

            return new Vector2Int(0, forward.z >= 0f ? 1 : -1);
        }

        return GetDirectionStep(connector.direction);
    }

    Vector2Int GetDirectionStep(RoomDoorDirection direction)
    {
        switch (direction)
        {
            case RoomDoorDirection.North:
                return new Vector2Int(0, 1);
            case RoomDoorDirection.South:
                return new Vector2Int(0, -1);
            case RoomDoorDirection.East:
                return new Vector2Int(1, 0);
            case RoomDoorDirection.West:
                return new Vector2Int(-1, 0);
            default:
                return Vector2Int.zero;
        }
    }

    bool IsGridCellOccupied(Vector2Int cell)
    {
        if (!useGridOccupancy)
            return false;

        RoomPlacement placement;
        if (!placementsByCell.TryGetValue(cell, out placement))
            return false;

        if (placement != null && placement.room != null)
            return true;

        placementsByCell.Remove(cell);
        return false;
    }

    bool IsConnectorTargetCellOccupied(RoomConnector connector)
    {
        if (!useGridOccupancy || connector == null || !connector.IsAvailable)
            return false;

        Vector2Int targetCell;
        return TryGetConnectorTargetCell(connector, out targetCell) && IsGridCellOccupied(targetCell);
    }

    Bounds CalculateRoomBounds(GameObject room)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        if (definition != null && definition.size.x > 0f && definition.size.y > 0f && definition.size.z > 0f)
            return definition.GetWorldBounds();

        Bounds bounds;
        if (TryCalculateChildBounds(room, out bounds))
            return bounds;

        return new Bounds(room != null ? room.transform.position : Vector3.zero, Vector3.one);
    }

    bool TryCalculateChildBounds(GameObject room, out Bounds bounds)
    {
        bounds = new Bounds(room != null ? room.transform.position : Vector3.zero, Vector3.zero);
        if (room == null) return false;

        bool hasBounds = false;

        Renderer[] renderers = room.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }

        Collider[] colliders = room.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null) continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
                bounds.Encapsulate(collider.bounds);
        }

        return hasBounds;
    }

    bool IntersectsExistingRoom(Bounds candidateBounds, out GameObject overlappingRoom)
    {
        overlappingRoom = null;
        Bounds candidate = ShrinkBounds(candidateBounds);

        foreach (KeyValuePair<GameObject, RoomPlacement> pair in placementsByRoom)
        {
            RoomPlacement placement = pair.Value;
            if (placement == null || placement.room == null) continue;

            Bounds existing = ShrinkBounds(placement.bounds);
            if (!candidate.Intersects(existing)) continue;

            overlappingRoom = placement.room;
            return true;
        }

        return false;
    }

    Bounds ShrinkBounds(Bounds bounds)
    {
        if (roomBoundsInset <= 0f)
            return bounds;

        Vector3 size = bounds.size;
        float inset = roomBoundsInset * 2f;
        size.x = Mathf.Max(0f, size.x - inset);
        size.z = Mathf.Max(0f, size.z - inset);

        return new Bounds(bounds.center, size);
    }

    void CloseBlockedConnector(RoomConnector connector)
    {
        if (connector == null) return;

        openConnectors.Remove(connector);
        connectorTargetCells.Remove(connector);

        if (connector.IsAvailable)
            connector.Close();
    }

    bool ShouldConsolidateAfterRoom(GameObject room)
    {
        if (isGeneratingFullMap)
            return false;

        return IsFinalRoom(room) ||
            maxGeneratedRooms > 0 && generatedRoomCount >= maxGeneratedRooms;
    }

    void ConsolidateGeneratedMap()
    {
        if (mapConsolidated)
            return;

        int adjacentBranchConnections = TryConnectAdjacentBranches();
        RecordAdjacentBranchConnections(adjacentBranchConnections);
        CleanOpenConnectors();

        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            GameObject room = spawnedRooms[i];
            RoomDefinition definition = GetRoomDefinition(room);
            if (definition == null || definition.connectors == null) continue;

            for (int j = 0; j < definition.connectors.Length; j++)
            {
                RoomConnector connector = definition.connectors[j];
                if (connector == null) continue;

                if (connector.IsAvailable)
                    CloseBlockedConnector(connector);
                else
                    connectorTargetCells.Remove(connector);
            }
        }

        openConnectors.Clear();
        connectorTargetCells.Clear();
        lastExitPoint = null;
        mapConsolidated = true;
    }

    int TryConnectAdjacentBranches()
    {
        if (!connectAdjacentBranches || adjacentBranchConnectionChance <= 0f)
            return 0;
        if (!useGridOccupancy || spawnedRooms.Count <= 1)
            return 0;

        CleanOpenConnectors();

        int connectedCount = 0;
        int maximumConnections = Mathf.Max(0, maximumAdjacentBranchConnections);
        List<RoomConnector> connectorsToCheck = new List<RoomConnector>(openConnectors);

        for (int i = 0; i < connectorsToCheck.Count; i++)
        {
            if (maximumConnections > 0 && connectedCount >= maximumConnections)
                break;

            RoomConnector source = connectorsToCheck[i];
            if (source == null || !source.IsAvailable)
                continue;
            if (Random.value > adjacentBranchConnectionChance)
                continue;

            RoomConnector exitConnector;
            RoomConnector entryConnector;
            if (!TryGetAdjacentBranchConnectionPair(
                source,
                out exitConnector,
                out entryConnector))
            {
                continue;
            }

            if (!ConnectRooms(exitConnector, entryConnector))
                continue;

            RecordDebugBranchConnection(exitConnector, entryConnector);
            CreateNavMeshLink(exitConnector, entryConnector);
            RefreshNavMeshLinkForConnector(exitConnector);
            connectedCount++;
        }

        if (connectedCount > 0)
        {
            Debug.Log(
                $"RoomGenerator connected {connectedCount} adjacent branch connector pair(s).");
        }

        return connectedCount;
    }

    void RecordDebugBranchConnection(
        RoomConnector first,
        RoomConnector second)
    {
        if (first == null || second == null)
            return;

        debugBranchConnections.Add(new ConnectorDebugConnection
        {
            first = first,
            second = second
        });
    }

    bool TryGetAdjacentBranchConnectionPair(
        RoomConnector source,
        out RoomConnector exitConnector,
        out RoomConnector entryConnector)
    {
        exitConnector = null;
        entryConnector = null;

        if (source == null || !source.IsAvailable)
            return false;

        RoomPlacement sourcePlacement;
        if (!TryGetConnectorSourcePlacement(source, out sourcePlacement))
            return false;

        Vector2Int targetCell;
        if (TryGetConnectorTargetCell(source, out targetCell))
        {
            RoomPlacement targetPlacement;
            if (placementsByCell.TryGetValue(targetCell, out targetPlacement) &&
                targetPlacement != null &&
                targetPlacement.room != null &&
                CanConnectAdjacentBranchRooms(sourcePlacement, targetPlacement))
            {
                RoomDefinition targetDefinition = GetRoomDefinition(targetPlacement.room);
                if (targetDefinition != null && targetDefinition.connectors != null)
                {
                    for (int i = 0; i < targetDefinition.connectors.Length; i++)
                    {
                        RoomConnector candidate = targetDefinition.connectors[i];
                        if (candidate == null ||
                            candidate == source ||
                            !candidate.IsAvailable)
                        {
                            continue;
                        }

                        Vector2Int candidateTargetCell;
                        if (!TryGetConnectorTargetCell(candidate, out candidateTargetCell))
                            continue;

                        if (candidateTargetCell != sourcePlacement.cell)
                            continue;

                        if (TryGetOrderedAdjacentBranchConnection(
                            source,
                            candidate,
                            out exitConnector,
                            out entryConnector))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return TryGetNearestPhysicalBranchConnectionPair(
            source,
            sourcePlacement,
            out exitConnector,
            out entryConnector);
    }

    bool TryGetNearestPhysicalBranchConnectionPair(
        RoomConnector source,
        RoomPlacement sourcePlacement,
        out RoomConnector exitConnector,
        out RoomConnector entryConnector)
    {
        exitConnector = null;
        entryConnector = null;

        if (source == null || sourcePlacement == null)
            return false;

        float bestScore = float.MaxValue;

        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            GameObject targetRoom = spawnedRooms[i];
            RoomPlacement targetPlacement;
            if (targetRoom == null ||
                !placementsByRoom.TryGetValue(targetRoom, out targetPlacement) ||
                !CanConnectAdjacentBranchRooms(sourcePlacement, targetPlacement))
            {
                continue;
            }

            RoomDefinition targetDefinition = GetRoomDefinition(targetRoom);
            if (targetDefinition == null || targetDefinition.connectors == null)
                continue;

            for (int j = 0; j < targetDefinition.connectors.Length; j++)
            {
                RoomConnector candidate = targetDefinition.connectors[j];
                if (candidate == null ||
                    candidate == source ||
                    !candidate.IsAvailable)
                {
                    continue;
                }

                RoomConnector orderedExit;
                RoomConnector orderedEntry;
                if (!TryGetOrderedAdjacentBranchConnection(
                    source,
                    candidate,
                    out orderedExit,
                    out orderedEntry))
                {
                    continue;
                }

                float score = GetConnectorDistanceScore(source, candidate);
                if (score >= bestScore)
                    continue;

                bestScore = score;
                exitConnector = orderedExit;
                entryConnector = orderedEntry;
            }
        }

        return exitConnector != null && entryConnector != null;
    }

    bool TryGetOrderedAdjacentBranchConnection(
        RoomConnector first,
        RoomConnector second,
        out RoomConnector exitConnector,
        out RoomConnector entryConnector)
    {
        exitConnector = null;
        entryConnector = null;

        if (first == null || second == null)
            return false;

        if (first.CanConnectTo(second) &&
            CanPhysicallyConnectAdjacentBranchDoors(first, second) &&
            HasClearDoorwayBetweenConnectors(first, second))
        {
            exitConnector = first;
            entryConnector = second;
            return true;
        }

        if (second.CanConnectTo(first) &&
            CanPhysicallyConnectAdjacentBranchDoors(second, first) &&
            HasClearDoorwayBetweenConnectors(second, first))
        {
            exitConnector = second;
            entryConnector = first;
            return true;
        }

        return false;
    }

    float GetConnectorDistanceScore(RoomConnector first, RoomConnector second)
    {
        if (first == null || second == null || first.Point == null || second.Point == null)
            return float.MaxValue;

        Vector3 delta = second.Point.position - first.Point.position;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }

    bool CanPhysicallyConnectAdjacentBranchDoors(
        RoomConnector exitConnector,
        RoomConnector entryConnector)
    {
        if (exitConnector == null || entryConnector == null)
            return false;

        Transform exitPoint = exitConnector.Point;
        Transform entryPoint = entryConnector.Point;
        if (exitPoint == null || entryPoint == null)
            return false;

        Vector3 delta = entryPoint.position - exitPoint.position;
        Vector3 horizontalDelta = new Vector3(delta.x, 0f, delta.z);
        float maxDistance = Mathf.Max(
            0.1f,
            maximumAdjacentBranchConnectionDoorDistance);

        if (horizontalDelta.sqrMagnitude > maxDistance * maxDistance)
            return false;

        float maxVerticalOffset = Mathf.Max(0.5f, doorwayClearanceHeight * 0.25f);
        if (Mathf.Abs(delta.y) > maxVerticalOffset)
            return false;

        if (horizontalDelta.sqrMagnitude <= 0.0001f)
            return true;

        Vector3 connectionDirection = horizontalDelta.normalized;
        Vector3 exitForward = GetHorizontalForward(exitPoint);
        Vector3 entryForward = GetHorizontalForward(entryPoint);

        if (exitForward != Vector3.zero &&
            Vector3.Dot(exitForward, connectionDirection) < 0.65f)
        {
            return false;
        }

        if (entryForward != Vector3.zero &&
            Vector3.Dot(entryForward, -connectionDirection) < 0.65f)
        {
            return false;
        }

        if (exitForward != Vector3.zero &&
            entryForward != Vector3.zero &&
            Vector3.Dot(exitForward, -entryForward) < 0.65f)
        {
            return false;
        }

        if (exitForward != Vector3.zero)
        {
            float forwardDistance = Vector3.Dot(horizontalDelta, exitForward);
            Vector3 lateralOffset =
                horizontalDelta - exitForward * forwardDistance;
            float maxLateralOffset =
                Mathf.Max(doorwayClearanceWidth, navMeshLinkWidth) * 0.5f;

            if (lateralOffset.magnitude > maxLateralOffset)
                return false;
        }

        return true;
    }

    Vector3 GetHorizontalForward(Transform point)
    {
        if (point == null)
            return Vector3.zero;

        Vector3 forward = point.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        return forward.normalized;
    }

    bool CanConnectAdjacentBranchRooms(
        RoomPlacement sourcePlacement,
        RoomPlacement targetPlacement)
    {
        if (sourcePlacement == null || targetPlacement == null)
            return false;
        if (sourcePlacement == targetPlacement)
            return false;
        if (sourcePlacement.room == null || targetPlacement.room == null)
            return false;
        if (spawnedRooms.Count > 0 &&
            (sourcePlacement.room == spawnedRooms[0] ||
             targetPlacement.room == spawnedRooms[0]))
        {
            return false;
        }
        if (BelongToSameDebugBranch(sourcePlacement.room, targetPlacement.room))
            return false;

        return !IsFinalRoom(sourcePlacement.room) &&
            !IsFinalRoom(targetPlacement.room);
    }

    bool BelongToSameDebugBranch(GameObject firstRoom, GameObject secondRoom)
    {
        RoomDebugInfo firstInfo = GetRoomDebugInfo(firstRoom);
        RoomDebugInfo secondInfo = GetRoomDebugInfo(secondRoom);

        if (firstInfo == null || secondInfo == null)
            return false;
        if (firstInfo.branchNumber <= 0 || secondInfo.branchNumber <= 0)
            return false;

        return firstInfo.branchNumber == secondInfo.branchNumber;
    }

    bool HasClearDoorwayBetweenConnectors(
        RoomConnector first,
        RoomConnector second)
    {
        if (!validateConnectedDoorwayClearance)
            return true;
        if (first == null || second == null)
            return false;

        Transform firstPoint = first.Point;
        Transform secondPoint = second.Point;
        if (firstPoint == null || secondPoint == null)
            return false;

        RoomDefinition firstDefinition = first.GetComponentInParent<RoomDefinition>();
        RoomDefinition secondDefinition = second.GetComponentInParent<RoomDefinition>();
        Transform firstRoom = firstDefinition != null ? firstDefinition.transform : null;
        Transform secondRoom = secondDefinition != null ? secondDefinition.transform : null;

        Physics.SyncTransforms();

        Vector3 center = (firstPoint.position + secondPoint.position) * 0.5f;
        Vector3 halfExtents = new Vector3(
            doorwayClearanceWidth * 0.5f,
            doorwayClearanceHeight * 0.5f,
            doorwayClearanceDepth * 0.5f);

        Collider[] overlaps = Physics.OverlapBox(
            center,
            halfExtents,
            firstPoint.rotation,
            doorwayBlockingLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider blocker = overlaps[i];
            if (blocker == null)
                continue;

            Transform blockerTransform = blocker.transform;
            bool belongsToFirstRoom = firstRoom != null &&
                blockerTransform.IsChildOf(firstRoom);
            bool belongsToSecondRoom = secondRoom != null &&
                blockerTransform.IsChildOf(secondRoom);

            if (!belongsToFirstRoom && !belongsToSecondRoom)
                continue;
            if (IsDoorwayIgnoredLayer(blocker.gameObject.layer))
                continue;
            if (ShouldIgnoreFloorLevelDoorwayBlocker(
                blocker,
                firstPoint,
                secondPoint))
            {
                continue;
            }

            RecordGenerationRejection(
                $"Adjacent doorway blocked by collider {GetColliderDebugName(blocker)}.");
            return false;
        }

        return true;
    }

    void RefreshNavMeshLinkForConnector(RoomConnector connector)
    {
        if (connector == null)
            return;

        NavMeshLink link;
        if (!navMeshLinksByConnector.TryGetValue(connector, out link) || link == null)
            return;

        ForceRefreshNavMeshLink(link);
    }

    void SpawnOrQueueRoomContent(GameObject room, int roomIndex)
    {
        if (room == null || contentSpawnedRooms.Contains(room))
            return;

        if (!isGeneratingFullMap && CanSpawnRoomContentNow())
        {
            SpawnRoomContent(room, roomIndex);
            return;
        }

        QueueRoomContentSpawn(room, roomIndex);
    }

    void QueueRoomContentSpawn(GameObject room, int roomIndex)
    {
        if (room == null)
            return;

        for (int i = 0; i < pendingRoomContentSpawns.Count; i++)
        {
            PendingRoomContentSpawn pending = pendingRoomContentSpawns[i];
            if (pending != null && pending.room == room)
                return;
        }

        pendingRoomContentSpawns.Add(new PendingRoomContentSpawn
        {
            room = room,
            roomIndex = roomIndex
        });
        StartRoomContentFlushWhenReady();
    }

    void SpawnRoomContent(GameObject room, int roomIndex)
    {
        if (room == null || contentSpawnedRooms.Contains(room))
            return;

        if (resourceSpawner != null)
            resourceSpawner.SpawnResourcesForRoom(room, roomIndex, seed);

        if (enemySpawner != null)
            enemySpawner.SpawnEnemiesForRoom(room, roomIndex, seed);

        contentSpawnedRooms.Add(room);
    }

    void FlushPendingRoomContentSpawns()
    {
        if (!CanSpawnRoomContentNow())
        {
            StartRoomContentFlushWhenReady();
            return;
        }

        for (int i = pendingRoomContentSpawns.Count - 1; i >= 0; i--)
        {
            PendingRoomContentSpawn pending = pendingRoomContentSpawns[i];
            pendingRoomContentSpawns.RemoveAt(i);

            if (pending == null || pending.room == null)
                continue;

            SpawnRoomContent(pending.room, pending.roomIndex);
        }

        if (pendingInitialTimeCamperSpawn)
        {
            pendingInitialTimeCamperSpawn = false;
            TrySpawnInitialTimeCamper();
        }
    }

    void StartRoomContentFlushWhenReady()
    {
        if (roomContentFlushCoroutine != null || !isActiveAndEnabled)
            return;
        if (!HasPendingRoomContentSpawns())
            return;

        roomContentFlushCoroutine = StartCoroutine(
            FlushRoomContentWhenReady());
    }

    IEnumerator FlushRoomContentWhenReady()
    {
        while (isActiveAndEnabled && HasPendingRoomContentSpawns())
        {
            if (CanSpawnRoomContentNow())
            {
                FlushPendingRoomContentSpawns();
                break;
            }

            yield return null;
        }

        roomContentFlushCoroutine = null;
    }

    void StopRoomContentFlushCoroutine()
    {
        if (roomContentFlushCoroutine == null)
            return;

        StopCoroutine(roomContentFlushCoroutine);
        roomContentFlushCoroutine = null;
    }

    bool HasPendingRoomContentSpawns()
    {
        return pendingInitialTimeCamperSpawn ||
            pendingRoomContentSpawns.Count > 0;
    }

    bool CanSpawnRoomContentNow()
    {
        bool multiplayerRun =
            RegionRunState.HasSelectedRegion &&
            RegionRunState.IsMultiplayer;
        NetworkManager networkManager = NetworkManager.Singleton;

        if (!multiplayerRun)
        {
            if (networkManager != null && networkManager.IsListening)
                return networkManager.IsServer;

            return true;
        }

        return networkManager != null &&
            networkManager.IsListening &&
            networkManager.IsServer;
    }

    void TrySpawnInitialTimeCamper()
    {
        if (initialEnemySpawned) return;
        if (!spawnTimeCamperAfterStartingRooms) return;
        if (spawnedRooms.Count < startingRoomCount) return;
        if (EnemySpawner.Instance == null) return;
        if (!CanSpawnRoomContentNow())
        {
            pendingInitialTimeCamperSpawn = true;
            StartRoomContentFlushWhenReady();
            return;
        }

        EnemySpawner.Instance.SpawnTimeCamper();
        initialEnemySpawned = true;
        pendingInitialTimeCamperSpawn = false;
    }

    void TrySpawnWaterValve()
    {
        if (!spawnWaterValveOnGeneratedMap) return;
        if (spawnedWaterValve != null) return;
        if (waterValvePrefab == null)
        {
            Debug.LogWarning("RoomGenerator cannot spawn the water valve because no prefab is assigned.");
            return;
        }

        if (!CanSpawnRoomContentNow())
            return;

        List<GameObject> eligibleRooms = GetWaterValveEligibleRooms();
        if (eligibleRooms.Count == 0)
        {
            Debug.LogWarning("RoomGenerator found no eligible room for the water valve.");
            return;
        }

        System.Random random = new System.Random(CreateWaterValveSeed());
        for (int attempt = 0; attempt < eligibleRooms.Count; attempt++)
        {
            int index = random.Next(eligibleRooms.Count);
            GameObject room = eligibleRooms[index];
            eligibleRooms.RemoveAt(index);

            Vector3 position;
            Quaternion rotation;
            if (!TryGetWaterValvePose(room, random, out position, out rotation))
                continue;

            SpawnWaterValve(position, rotation);
            return;
        }

        Debug.LogWarning("RoomGenerator could not find a valid wall pose for the water valve.");
    }

    List<GameObject> GetWaterValveEligibleRooms()
    {
        List<GameObject> eligibleRooms = new List<GameObject>();
        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            GameObject room = spawnedRooms[i];
            if (room == null)
                continue;

            if (i == 0)
                continue;

            RoomDefinition definition = GetRoomDefinition(room);
            if (definition == null)
                continue;

            if (definition.category == RoomCategory.SubmarineSpawn ||
                definition.category == RoomCategory.Final)
            {
                continue;
            }

            eligibleRooms.Add(room);
        }

        return eligibleRooms;
    }

    bool TryGetWaterValvePose(
        GameObject room,
        System.Random random,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (room == null)
            return false;

        RoomDefinition definition = GetRoomDefinition(room);
        if (definition != null)
        {
            int attempts = Mathf.Max(1, waterValveWallProbeAttempts);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int wall = random.Next(4);
                RaycastHit hit;
                Vector3 rayDirection;
                if (!TryGetWaterValveWallHit(
                    room,
                    definition,
                    random,
                    wall,
                    out hit,
                    out rayDirection))
                {
                    continue;
                }

                Vector3 wallNormal = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
                if (wallNormal.sqrMagnitude <= 0.0001f)
                    wallNormal = -rayDirection;

                wallNormal.Normalize();

                if (Vector3.Dot(wallNormal, rayDirection) > 0f)
                    wallNormal = -wallNormal;

                BuildWaterValvePoseFromWall(hit.point, wallNormal, out position, out rotation);
                return true;
            }
        }

        return TryGetWaterValveBoundsPose(room, random, out position, out rotation);
    }

    bool TryGetWaterValveWallHit(
        GameObject room,
        RoomDefinition definition,
        System.Random random,
        int wall,
        out RaycastHit selectedHit,
        out Vector3 rayDirection)
    {
        selectedHit = default;
        rayDirection = Vector3.forward;

        if (room == null || definition == null)
            return false;

        Vector3 halfSize = definition.size * 0.5f;
        if (halfSize.x <= 0.1f || halfSize.z <= 0.1f)
            return false;

        float minX = definition.boundsCenter.x - halfSize.x;
        float maxX = definition.boundsCenter.x + halfSize.x;
        float minZ = definition.boundsCenter.z - halfSize.z;
        float maxZ = definition.boundsCenter.z + halfSize.z;
        float paddedMinX = minX + waterValveSidePadding;
        float paddedMaxX = maxX - waterValveSidePadding;
        float paddedMinZ = minZ + waterValveSidePadding;
        float paddedMaxZ = maxZ - waterValveSidePadding;
        float localY = definition.boundsCenter.y - halfSize.y + waterValveWallHeight;
        Vector3 localOrigin = new Vector3(definition.boundsCenter.x, localY, definition.boundsCenter.z);
        float distance;

        switch (wall)
        {
            case 0:
                rayDirection = -room.transform.right;
                localOrigin.z = RandomRange(random, paddedMinZ, paddedMaxZ, definition.boundsCenter.z);
                distance = halfSize.x + waterValveWallInset + 0.5f;
                break;
            case 1:
                rayDirection = room.transform.right;
                localOrigin.z = RandomRange(random, paddedMinZ, paddedMaxZ, definition.boundsCenter.z);
                distance = halfSize.x + waterValveWallInset + 0.5f;
                break;
            case 2:
                rayDirection = -room.transform.forward;
                localOrigin.x = RandomRange(random, paddedMinX, paddedMaxX, definition.boundsCenter.x);
                distance = halfSize.z + waterValveWallInset + 0.5f;
                break;
            default:
                rayDirection = room.transform.forward;
                localOrigin.x = RandomRange(random, paddedMinX, paddedMaxX, definition.boundsCenter.x);
                distance = halfSize.z + waterValveWallInset + 0.5f;
                break;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            room.transform.TransformPoint(localOrigin),
            rayDirection,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (!IsWaterValveWallHit(room, definition, wall, hit))
                continue;

            selectedHit = hit;
            return true;
        }

        return false;
    }

    bool IsWaterValveWallHit(
        GameObject room,
        RoomDefinition definition,
        int wall,
        RaycastHit hit)
    {
        if (room == null || definition == null || hit.collider == null)
            return false;

        Transform hitTransform = hit.collider.transform;
        if (!hitTransform.IsChildOf(room.transform))
            return false;

        if (hit.collider.GetComponentInParent<RoomConnector>() != null ||
            hit.collider.GetComponentInParent<DoorTrigger>() != null)
        {
            return false;
        }

        Vector3 halfSize = definition.size * 0.5f;
        float minX = definition.boundsCenter.x - halfSize.x;
        float maxX = definition.boundsCenter.x + halfSize.x;
        float minZ = definition.boundsCenter.z - halfSize.z;
        float maxZ = definition.boundsCenter.z + halfSize.z;
        Vector3 localPoint = room.transform.InverseTransformPoint(hit.point);
        float boundaryTolerance = Mathf.Max(0.35f, waterValveWallInset + 0.35f);

        switch (wall)
        {
            case 0:
                return Mathf.Abs(localPoint.x - minX) <= boundaryTolerance;
            case 1:
                return Mathf.Abs(localPoint.x - maxX) <= boundaryTolerance;
            case 2:
                return Mathf.Abs(localPoint.z - minZ) <= boundaryTolerance;
            default:
                return Mathf.Abs(localPoint.z - maxZ) <= boundaryTolerance;
        }
    }

    bool TryGetWaterValveBoundsPose(
        GameObject room,
        System.Random random,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (room == null)
            return false;

        RoomDefinition definition = GetRoomDefinition(room);
        Bounds bounds = definition != null
            ? definition.GetWorldBounds()
            : new Bounds(room.transform.position, Vector3.one * 8f);

        Vector3 extents = bounds.extents;
        if (extents.x <= 0.1f || extents.z <= 0.1f)
            return false;

        int wall = random.Next(4);
        float paddedMinX = bounds.min.x + waterValveSidePadding;
        float paddedMaxX = bounds.max.x - waterValveSidePadding;
        float paddedMinZ = bounds.min.z + waterValveSidePadding;
        float paddedMaxZ = bounds.max.z - waterValveSidePadding;
        float x = RandomRange(random, paddedMinX, paddedMaxX, bounds.center.x);
        float z = RandomRange(random, paddedMinZ, paddedMaxZ, bounds.center.z);
        float y = bounds.min.y + waterValveWallHeight;
        Vector3 outwardNormal;

        switch (wall)
        {
            case 0:
                position = new Vector3(bounds.min.x + waterValveWallInset, y, z);
                outwardNormal = Vector3.left;
                break;
            case 1:
                position = new Vector3(bounds.max.x - waterValveWallInset, y, z);
                outwardNormal = Vector3.right;
                break;
            case 2:
                position = new Vector3(x, y, bounds.min.z + waterValveWallInset);
                outwardNormal = Vector3.back;
                break;
            default:
                position = new Vector3(x, y, bounds.max.z - waterValveWallInset);
                outwardNormal = Vector3.forward;
                break;
        }

        BuildWaterValvePoseFromWall(position, -outwardNormal, out position, out rotation);
        return true;
    }

    void BuildWaterValvePoseFromWall(
        Vector3 wallPoint,
        Vector3 outwardNormal,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = wallPoint;
        rotation = Quaternion.identity;

        if (outwardNormal.sqrMagnitude <= 0.0001f)
            outwardNormal = Vector3.forward;

        outwardNormal.Normalize();

        Vector3 localBackPoint;
        Vector3 localForward;
        if (TryGetWaterValveMarkerData(out localBackPoint, out localForward))
        {
            rotation = Quaternion.FromToRotation(localForward, outwardNormal) *
                Quaternion.Euler(waterValveRotationOffset);
            position = wallPoint - rotation * localBackPoint;
            return;
        }

        rotation = Quaternion.LookRotation(outwardNormal, Vector3.up) *
            Quaternion.Euler(waterValveRotationOffset);
        position = wallPoint + outwardNormal * waterValveWallInset;
    }

    bool TryGetWaterValveMarkerData(out Vector3 localBackPoint, out Vector3 localForward)
    {
        localBackPoint = Vector3.zero;
        localForward = Vector3.forward;

        if (waterValvePrefab == null)
            return false;

        Transform root = waterValvePrefab.transform;
        Transform back = FindChildRecursive(root, "Back");
        Transform front = FindChildRecursive(root, "Front");
        if (back == null || front == null)
            return false;

        localBackPoint = root.InverseTransformPoint(back.position);
        Vector3 localFrontPoint = root.InverseTransformPoint(front.position);
        localForward = localFrontPoint - localBackPoint;

        if (localForward.sqrMagnitude <= 0.0001f)
            return false;

        localForward.Normalize();
        return true;
    }

    Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Transform result = FindChildRecursive(child, childName);
            if (result != null)
                return result;
        }

        return null;
    }

    float RandomRange(
        System.Random random,
        float min,
        float max,
        float fallback)
    {
        if (random == null || min >= max)
            return fallback;

        return Mathf.Lerp(min, max, (float)random.NextDouble());
    }

    void SpawnWaterValve(Vector3 position, Quaternion rotation)
    {
        spawnedWaterValve = Instantiate(waterValvePrefab, position, rotation);
        spawnedWaterValve.name = waterValvePrefab.name;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return;

        if (!networkManager.IsServer)
            return;

        NetworkObject networkObject = spawnedWaterValve.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogWarning("Water valve prefab needs a NetworkObject for multiplayer spawning.");
            return;
        }

        if (!networkObject.IsSpawned)
            networkObject.Spawn(true);
    }

    int CreateWaterValveSeed()
    {
        unchecked
        {
            return seed * 397 ^ 0x4D61566;
        }
    }

    bool AlignRoomToExpansion(
        GameObject room,
        RoomConnector expansionConnector,
        Transform expansionPoint)
    {
        if (expansionPoint == null) return true;

        RoomConnector entryConnector = expansionConnector != null
            ? GetCompatibleEntranceConnector(room, expansionConnector)
            : null;

        Transform entryPoint = entryConnector != null ? entryConnector.Point : GetRoomEntrancePoint(room);
        if (entryPoint == null)
        {
            Debug.LogWarning(room.name + " is missing an entrance point. Add a RoomDefinition entrance connector or DoorPoint_A.");
            return false;
        }

        Quaternion rotationDiff = expansionPoint.rotation * Quaternion.Inverse(entryPoint.rotation);
        rotationDiff *= Quaternion.Euler(0f, 180f, 0f);
        room.transform.rotation = rotationDiff;

        Vector3 offset = expansionPoint.position - entryPoint.position;
        room.transform.position += offset;

        return true;
    }

    bool HasClearConnectedDoorway(
        GameObject room,
        RoomConnector expansionConnector)
    {
        if (!validateConnectedDoorwayClearance)
            return true;
        if (room == null || expansionConnector == null)
            return true;

        RoomConnector entryConnector = GetCompatibleEntranceConnector(
            room,
            expansionConnector);
        if (entryConnector == null)
            return false;

        Transform exitPoint = expansionConnector.Point;
        Transform entryPoint = entryConnector.Point;
        if (exitPoint == null || entryPoint == null)
            return false;

        Physics.SyncTransforms();

        Vector3 center =
            (exitPoint.position + entryPoint.position) * 0.5f;

        Vector3 halfExtents = new Vector3(
            doorwayClearanceWidth * 0.5f,
            doorwayClearanceHeight * 0.5f,
            doorwayClearanceDepth * 0.5f);

        Collider[] overlaps = Physics.OverlapBox(
            center,
            halfExtents,
            exitPoint.rotation,
            doorwayBlockingLayers,
            QueryTriggerInteraction.Ignore);

        RoomDefinition sourceDefinition =
            expansionConnector.GetComponentInParent<RoomDefinition>();
        Transform sourceRoom = sourceDefinition != null
            ? sourceDefinition.transform
            : null;

        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider blocker = overlaps[i];
            if (blocker == null)
                continue;

            Transform blockerTransform = blocker.transform;
            bool belongsToNewRoom =
                blockerTransform.IsChildOf(room.transform);
            bool belongsToSourceRoom =
                sourceRoom != null &&
                blockerTransform.IsChildOf(sourceRoom);

            if (!belongsToNewRoom && !belongsToSourceRoom)
                continue;

            if (IsDoorwayIgnoredLayer(blocker.gameObject.layer))
                continue;

            if (ShouldIgnoreFloorLevelDoorwayBlocker(
                blocker,
                exitPoint,
                entryPoint))
            {
                continue;
            }

            RecordGenerationRejection(
                $"Doorway blocked by collider {GetColliderDebugName(blocker)}.");
            return false;
        }

        return true;
    }

    bool IsDoorwayIgnoredLayer(int layer)
    {
        return (doorwayIgnoredLayers.value & (1 << layer)) != 0;
    }

    bool ShouldIgnoreFloorLevelDoorwayBlocker(
        Collider blocker,
        Transform exitPoint,
        Transform entryPoint)
    {
        if (!ignoreFloorLevelDoorwayBlockers || blocker == null)
            return false;
        if (exitPoint == null || entryPoint == null)
            return false;

        float doorwayBaseY = Mathf.Min(
            exitPoint.position.y,
            entryPoint.position.y);

        return blocker.bounds.max.y <=
            doorwayBaseY + doorwayFloorBlockerTolerance;
    }

    string GetColliderDebugName(Collider collider)
    {
        if (collider == null)
            return "missing collider";

        return collider.gameObject.name;
    }

    bool FinalizeRoomConnection(GameObject room, RoomConnector expansionConnector)
    {
        if (expansionConnector == null)
            return true;

        RoomConnector entryConnector = GetCompatibleEntranceConnector(room, expansionConnector);
        if (entryConnector != null)
            return ConnectRooms(expansionConnector, entryConnector);

        CloseBlockedConnector(expansionConnector);
        return true;
    }

    bool ConnectRooms(RoomConnector exitConnector, RoomConnector entryConnector)
    {
        if (exitConnector == null || entryConnector == null)
            return true;

        if (!exitConnector.TryConnect(entryConnector))
        {
            Debug.LogWarning(
                $"Could not connect room connectors {exitConnector.name} and {entryConnector.name}. Check direction, state, and entrance/exit settings.");
            return false;
        }

        openConnectors.Remove(exitConnector);
        openConnectors.Remove(entryConnector);
        connectorTargetCells.Remove(exitConnector);
        connectorTargetCells.Remove(entryConnector);
        return true;
    }

    void CreateNavMeshLink(RoomConnector first, RoomConnector second)
    {
        if (!createNavMeshLinksBetweenRooms) return;
        if (first == null || second == null) return;
        if (navMeshLinksByConnector.ContainsKey(first) ||
            navMeshLinksByConnector.ContainsKey(second))
        {
            return;
        }

        Transform firstPoint = first.Point;
        Transform secondPoint = second.Point;
        if (firstPoint == null || secondPoint == null)
            return;

        Transform root = GetNavMeshLinkRoot();
        Vector3 firstWorld = firstPoint.position;
        Vector3 secondWorld = secondPoint.position;
        Vector3 centerWorld = (firstWorld + secondWorld) * 0.5f;
        centerWorld.y = navMeshLinkWorldHeight;

        Vector3 linkDirection = secondWorld - firstWorld;
        linkDirection.y = 0f;
        if (linkDirection.sqrMagnitude <= 0.0001f)
        {
            linkDirection = firstPoint.forward;
            linkDirection.y = 0f;
        }
        if (linkDirection.sqrMagnitude <= 0.0001f)
            linkDirection = Vector3.forward;

        GameObject linkObject = new GameObject(
            $"NavMeshLink_{first.name}_to_{second.name}");
        linkObject.transform.SetParent(root, worldPositionStays: false);
        linkObject.transform.position = centerWorld;
        linkObject.transform.rotation = Quaternion.LookRotation(
            linkDirection.normalized,
            Vector3.up);

        NavMeshLink link = linkObject.AddComponent<NavMeshLink>();
        link.agentTypeID = 0;
        link.area = 0;
        link.costModifier = -1f;
        link.width = navMeshLinkWidth;
        link.autoUpdate = true;
        link.bidirectional = true;
        link.activated = true;
        link.startTransform = null;
        link.endTransform = null;
        link.startPoint = new Vector3(0f, 0f, -navMeshLinkHalfLength);
        link.endPoint = new Vector3(0f, 0f, navMeshLinkHalfLength);

        navMeshLinksByConnector[first] = link;
        navMeshLinksByConnector[second] = link;
    }

    Transform GetNavMeshLinkRoot()
    {
        if (navMeshLinkRoot != null)
            return navMeshLinkRoot;

        GameObject root = new GameObject("Generated NavMesh Links");
        root.transform.SetParent(transform, worldPositionStays: false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        navMeshLinkRoot = root.transform;
        return navMeshLinkRoot;
    }

    void RegisterRoomDoors(GameObject room)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        bool registeredConnectorDoor = false;

        if (definition != null && definition.connectors != null)
        {
            for (int i = 0; i < definition.connectors.Length; i++)
            {
                RoomConnector connector = definition.connectors[i];
                if (connector == null || !connector.canBeExit || !connector.IsAvailable) continue;

                DoorTrigger trigger = connector.Trigger;
                if (trigger == null) continue;

                trigger.Initialize(this);
                registeredConnectorDoor = true;
            }
        }

        if (registeredConnectorDoor)
            return;

        DoorTrigger exitTrigger = GetRoomExitTrigger(room);
        if (exitTrigger != null)
            exitTrigger.Initialize(this);
        else if (!IsFinalRoom(room))
            Debug.LogWarning(room.name + " is missing an exit DoorTrigger. Add it to RoomDefinition or DoorTrigger_B.");
    }

    Transform GetRoomEntrancePoint(GameObject room)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        Transform point;
        if (definition != null && definition.TryGetEntrancePoint(out point))
            return point;

        return room.transform.Find("DoorPoint_A");
    }

    RoomConnector GetCompatibleEntranceConnector(GameObject room, RoomConnector expansionConnector)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        RoomConnector connector;
        if (definition != null && definition.TryGetEntranceConnector(expansionConnector, out connector))
            return connector;

        return null;
    }

    Transform GetRoomExitPoint(GameObject room)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        Transform point;
        if (definition != null && definition.TryGetExitPoint(out point))
            return point;

        return room.transform.Find("DoorPoint_B");
    }

    RoomConnector GetRoomExitConnector(GameObject room)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        RoomConnector connector;
        if (definition != null && definition.TryGetExitConnector(out connector))
            return connector;

        return null;
    }

    DoorTrigger GetRoomExitTrigger(GameObject room)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        DoorTrigger trigger;
        if (definition != null && definition.TryGetExitTrigger(out trigger))
            return trigger;

        return room.transform.Find("DoorTrigger_B")?.GetComponent<DoorTrigger>();
    }

    RoomDefinition GetRoomDefinition(GameObject room)
    {
        if (room == null) return null;

        RoomDefinition definition = room.GetComponent<RoomDefinition>();
        if (definition == null)
            definition = room.GetComponentInChildren<RoomDefinition>(true);

        return definition;
    }

    void AddOpenConnectors(GameObject room)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        if (definition == null || definition.connectors == null)
            return;

        for (int i = 0; i < definition.connectors.Length; i++)
        {
            RoomConnector connector = definition.connectors[i];
            if (connector == null || !connector.canBeExit || !connector.IsAvailable) continue;
            if (openConnectors.Contains(connector)) continue;

            Vector2Int targetCell;
            if (!TryGetConnectorTargetCell(connector, out targetCell))
            {
                CloseBlockedConnector(connector);
                continue;
            }

            if (IsGridCellOccupied(targetCell) &&
                !ShouldKeepOccupiedConnectorForBranchConnection(connector))
            {
                CloseBlockedConnector(connector);
                continue;
            }

            openConnectors.Add(connector);
        }
    }

    void CleanOpenConnectors()
    {
        for (int i = openConnectors.Count - 1; i >= 0; i--)
        {
            RoomConnector connector = openConnectors[i];
            if (connector == null || !connector.canBeExit || !connector.IsAvailable)
            {
                if (connector != null)
                    connectorTargetCells.Remove(connector);

                openConnectors.RemoveAt(i);
                continue;
            }

            if (IsConnectorTargetCellOccupied(connector) &&
                !ShouldKeepOccupiedConnectorForBranchConnection(connector))
            {
                CloseBlockedConnector(connector);
            }
        }
    }

    bool ShouldKeepOccupiedConnectorForBranchConnection(RoomConnector connector)
    {
        return connectAdjacentBranches &&
            generateFullMapOnStart &&
            connector != null &&
            connector.IsAvailable;
    }

    void RemoveOpenConnectorsForRoom(GameObject room)
    {
        if (room == null) return;

        RoomDefinition definition = GetRoomDefinition(room);
        if (definition != null && definition.connectors != null)
        {
            for (int i = 0; i < definition.connectors.Length; i++)
            {
                RoomConnector connector = definition.connectors[i];
                openConnectors.Remove(connector);
                connectorTargetCells.Remove(connector);
                RemoveNavMeshLink(connector);
            }
        }

        UnregisterRoomPlacement(room);

        if (lastExitPoint != null && lastExitPoint.IsChildOf(room.transform))
            lastExitPoint = null;
    }

    void RemoveNavMeshLink(RoomConnector connector)
    {
        if (connector == null) return;

        NavMeshLink link;
        if (!navMeshLinksByConnector.TryGetValue(connector, out link))
            return;

        List<RoomConnector> linkedConnectors = new List<RoomConnector>();
        foreach (KeyValuePair<RoomConnector, NavMeshLink> pair in navMeshLinksByConnector)
        {
            if (pair.Value == link)
                linkedConnectors.Add(pair.Key);
        }

        for (int i = 0; i < linkedConnectors.Count; i++)
            navMeshLinksByConnector.Remove(linkedConnectors[i]);

        if (link != null)
            Destroy(link.gameObject);
    }

    void UpdateLegacyExitPoint(GameObject room)
    {
        RoomConnector exitConnector = GetRoomExitConnector(room);
        if (exitConnector != null)
        {
            lastExitPoint = exitConnector.Point;
            return;
        }

        Transform exitPoint = GetRoomExitPoint(room);
        if (exitPoint != null)
            lastExitPoint = exitPoint;
        else
        {
            lastExitPoint = null;
            if (!IsFinalRoom(room))
                Debug.LogWarning(room.name + " is missing an exit point. Add a RoomDefinition exit connector or DoorPoint_B.");
        }
    }

    bool IsFinalRoom(GameObject room)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        return definition != null && definition.category == RoomCategory.Final;
    }

    void RegisterRoomNavMesh(GameObject room)
    {
        if (EnemySpawner.Instance == null) return;

        NavMeshSurface surface = room.GetComponent<NavMeshSurface>();
        if (surface == null)
            surface = room.GetComponentInChildren<NavMeshSurface>(true);

        if (surface == null)
        {
            Debug.LogWarning(room.name + " is missing a NavMeshSurface.");
            return;
        }

        surface.enabled = true;
        Physics.SyncTransforms();
        registeredSurfaces.Add(surface);
        EnemySpawner.Instance.RegisterSurface(surface, buildNow: true);
        CreateNavMeshLinksForRoom(room);
        RefreshNavMeshLinksForRoom(room);
        StartCoroutine(RefreshNavMeshLinksForRoomAfterNavMeshUpdate(room));
    }

    void CreateNavMeshLinksForRoom(GameObject room)
    {
        if (!createNavMeshLinksBetweenRooms || room == null)
            return;

        RoomDefinition definition = GetRoomDefinition(room);
        if (definition == null || definition.connectors == null)
            return;

        for (int i = 0; i < definition.connectors.Length; i++)
        {
            RoomConnector connector = definition.connectors[i];
            if (connector == null || connector.ConnectedTo == null)
                continue;

            CreateNavMeshLink(connector, connector.ConnectedTo);
        }
    }

    void RefreshNavMeshLinksForRoom(GameObject room)
    {
        if (!createNavMeshLinksBetweenRooms || room == null)
            return;

        RoomDefinition definition = GetRoomDefinition(room);
        if (definition == null || definition.connectors == null)
            return;

        List<NavMeshLink> refreshedLinks = new List<NavMeshLink>();
        for (int i = 0; i < definition.connectors.Length; i++)
        {
            RoomConnector connector = definition.connectors[i];
            if (connector == null) continue;

            NavMeshLink link;
            if (!navMeshLinksByConnector.TryGetValue(connector, out link))
                continue;
            if (link == null || refreshedLinks.Contains(link))
                continue;

            ForceRefreshNavMeshLink(link);
            refreshedLinks.Add(link);
        }
    }

    IEnumerator RefreshNavMeshLinksForRoomAfterNavMeshUpdate(GameObject room)
    {
        yield return null;
        RefreshNavMeshLinksForRoom(room);

        yield return null;
        RefreshNavMeshLinksForRoom(room);
    }

    void ForceRefreshNavMeshLink(NavMeshLink link)
    {
        if (link == null)
            return;

        link.UpdateLink();

        Transform linkTransform = link.transform;
        Vector3 originalPosition = linkTransform.position;
        linkTransform.position = originalPosition + Vector3.up * 0.001f;
        link.UpdateLink();

        linkTransform.position = originalPosition;
        link.UpdateLink();
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGeneratedMapGizmos)
            return;

        Dictionary<GameObject, int> distances = BuildRoomDistanceMap();
        DrawGeneratedRoomBoundsGizmos();
        DrawGeneratedRoomMarkerGizmos();
        DrawConnectedConnectorGizmos();
        DrawClosedConnectorGizmos();
        DrawGeneratedRoomLabels(distances);
    }

    void DrawGeneratedRoomBoundsGizmos()
    {
        if (placementsByRoom == null)
            return;

        foreach (KeyValuePair<GameObject, RoomPlacement> pair in placementsByRoom)
        {
            RoomPlacement placement = pair.Value;
            if (placement == null || placement.room == null)
                continue;

            Gizmos.color = GetRoomBoundsGizmoColor(placement.room);
            Gizmos.DrawWireCube(placement.bounds.center, placement.bounds.size);
        }
    }

    void DrawConnectedConnectorGizmos()
    {
        if (spawnedRooms == null)
            return;

        List<RoomConnector> drawnConnectors = new List<RoomConnector>();

        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            RoomDefinition definition = GetRoomDefinition(spawnedRooms[i]);
            if (definition == null || definition.connectors == null)
                continue;

            for (int j = 0; j < definition.connectors.Length; j++)
            {
                RoomConnector connector = definition.connectors[j];
                if (connector == null ||
                    connector.State != RoomConnectorState.Connected ||
                    connector.ConnectedTo == null ||
                    drawnConnectors.Contains(connector))
                {
                    continue;
                }

                drawnConnectors.Add(connector);
                drawnConnectors.Add(connector.ConnectedTo);

                bool isBranchConnection = IsDebugBranchConnection(
                    connector,
                    connector.ConnectedTo);
                Gizmos.color = isBranchConnection && drawBranchConnectionGizmos
                    ? new Color(1f, 0.45f, 0.05f, 1f)
                    : new Color(0.25f, 0.9f, 1f, 0.9f);

                Gizmos.DrawLine(
                    connector.Point.position,
                    connector.ConnectedTo.Point.position);

                if (isBranchConnection && drawBranchConnectionGizmos)
                {
                    Vector3 midpoint =
                        (connector.Point.position + connector.ConnectedTo.Point.position) *
                        0.5f;
                    Gizmos.DrawWireSphere(
                        midpoint,
                        generationDebugMarkerSize * 0.45f);
                    DrawBranchConnectionVerticalMarker(connector, Gizmos.color);
                    DrawBranchConnectionVerticalMarker(connector.ConnectedTo, Gizmos.color);
                }
            }
        }
    }

    void DrawBranchConnectionVerticalMarker(
        RoomConnector connector,
        Color color)
    {
        if (connector == null || connector.Point == null)
            return;

        RoomPlacement placement;
        if (!TryGetConnectorSourcePlacement(connector, out placement) ||
            placement == null)
        {
            return;
        }

        float markerSize = Mathf.Max(0.1f, generationDebugMarkerSize);
        float markerHeight = Mathf.Max(2f, markerSize * 3f);
        Vector3 basePosition = connector.Point.position;
        basePosition.y = placement.bounds.max.y + markerSize * 0.25f;
        Vector3 topPosition = basePosition + Vector3.up * markerHeight;

        Gizmos.color = color;
        Gizmos.DrawLine(basePosition, topPosition);
        Gizmos.DrawWireSphere(topPosition, markerSize * 0.45f);
        Gizmos.DrawLine(
            topPosition - Vector3.right * markerSize * 0.5f,
            topPosition + Vector3.right * markerSize * 0.5f);
        Gizmos.DrawLine(
            topPosition - Vector3.forward * markerSize * 0.5f,
            topPosition + Vector3.forward * markerSize * 0.5f);
    }

    void DrawGeneratedRoomMarkerGizmos()
    {
        if (placementsByRoom == null)
            return;

        float markerSize = Mathf.Max(0.1f, generationDebugMarkerSize);

        foreach (KeyValuePair<GameObject, RoomPlacement> pair in placementsByRoom)
        {
            GameObject room = pair.Key;
            RoomPlacement placement = pair.Value;
            if (room == null || placement == null)
                continue;

            RoomDebugInfo info = GetRoomDebugInfo(room);
            Vector3 center = placement.bounds.center;
            Vector3 markerPosition =
                center + Vector3.up * (placement.bounds.extents.y + markerSize * 0.5f);

            if (info != null && info.isStart)
            {
                Gizmos.color = new Color(0.1f, 1f, 0.35f, 0.95f);
                Gizmos.DrawCube(
                    markerPosition,
                    Vector3.one * markerSize);
                continue;
            }

            if (IsFinalRoom(room))
            {
                Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.95f);
                Gizmos.DrawSphere(markerPosition, markerSize * 0.55f);
                continue;
            }

            Gizmos.color = GetRoomBoundsGizmoColor(room);
            Gizmos.DrawWireSphere(markerPosition, markerSize * 0.45f);
        }
    }

    void DrawClosedConnectorGizmos()
    {
        if (!drawClosedConnectorGizmos || spawnedRooms == null)
            return;

        float markerSize = Mathf.Max(0.1f, generationDebugMarkerSize) * 0.45f;
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.95f);

        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            RoomDefinition definition = GetRoomDefinition(spawnedRooms[i]);
            if (definition == null || definition.connectors == null)
                continue;

            for (int j = 0; j < definition.connectors.Length; j++)
            {
                RoomConnector connector = definition.connectors[j];
                if (connector == null ||
                    connector.State != RoomConnectorState.Closed ||
                    connector.Point == null)
                {
                    continue;
                }

                Vector3 position = connector.Point.position;
                Gizmos.DrawCube(position, Vector3.one * markerSize);
                Gizmos.DrawRay(
                    position,
                    connector.Point.forward * generationDebugMarkerSize);
            }
        }
    }

    void DrawGeneratedRoomLabels(Dictionary<GameObject, int> distances)
    {
#if UNITY_EDITOR
        if (!drawGeneratedMapLabels || placementsByRoom == null)
            return;

        GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = Color.white;

        foreach (KeyValuePair<GameObject, RoomPlacement> pair in placementsByRoom)
        {
            GameObject room = pair.Key;
            RoomPlacement placement = pair.Value;
            if (room == null || placement == null)
                continue;

            Vector3 labelPosition =
                placement.bounds.center +
                Vector3.up * (placement.bounds.extents.y + generationDebugLabelHeight);

            Handles.color = GetRoomBoundsGizmoColor(room);
            Handles.Label(
                labelPosition,
                GetRoomDebugLabel(room, placement, distances),
                style);
        }
#endif
    }

    string GetRoomDebugLabel(
        GameObject room,
        RoomPlacement placement,
        Dictionary<GameObject, int> distances)
    {
        RoomDebugInfo info = GetRoomDebugInfo(room);
        int distance = -1;
        if (distances != null && room != null)
            distances.TryGetValue(room, out distance);

        StringBuilder builder = new StringBuilder(64);

        if (info != null && info.isStart)
        {
            builder.Append("START");
        }
        else if (IsFinalRoom(room))
        {
            builder.Append("FINAL");
            if (info != null)
                builder.Append($" B{info.branchNumber:00}");
        }
        else if (info != null && info.branchNumber > 0)
        {
            builder.Append($"B{info.branchNumber:00} R{info.branchRoomNumber:00}");
        }
        else if (info != null)
        {
            builder.Append($"ROOM {info.roomIndex:00}");
        }
        else
        {
            builder.Append("ROOM");
        }

        if (distance >= 0)
            builder.Append($"\nD{distance}");

        if (placement != null)
            builder.Append($"\n{placement.cell.x},{placement.cell.y}");

        return builder.ToString();
    }

    Dictionary<GameObject, int> BuildRoomDistanceMap()
    {
        Dictionary<GameObject, int> distances = new Dictionary<GameObject, int>();
        if (spawnedRooms == null || spawnedRooms.Count == 0 || spawnedRooms[0] == null)
            return distances;

        Queue<GameObject> queue = new Queue<GameObject>();
        GameObject startRoom = spawnedRooms[0];
        distances[startRoom] = 0;
        queue.Enqueue(startRoom);

        while (queue.Count > 0)
        {
            GameObject currentRoom = queue.Dequeue();
            int currentDistance = distances[currentRoom];

            RoomDefinition definition = GetRoomDefinition(currentRoom);
            if (definition == null || definition.connectors == null)
                continue;

            for (int i = 0; i < definition.connectors.Length; i++)
            {
                GameObject connectedRoom = GetConnectedRoom(definition.connectors[i]);
                if (connectedRoom == null || distances.ContainsKey(connectedRoom))
                    continue;

                distances[connectedRoom] = currentDistance + 1;
                queue.Enqueue(connectedRoom);
            }
        }

        return distances;
    }

    bool IsDebugBranchConnection(RoomConnector first, RoomConnector second)
    {
        if (first == null || second == null || debugBranchConnections == null)
            return false;

        for (int i = 0; i < debugBranchConnections.Count; i++)
        {
            ConnectorDebugConnection connection = debugBranchConnections[i];
            if (connection == null)
                continue;

            bool sameOrder = connection.first == first &&
                connection.second == second;
            bool inverseOrder = connection.first == second &&
                connection.second == first;
            if (sameOrder || inverseOrder)
                return true;
        }

        return false;
    }

    RoomDebugInfo GetRoomDebugInfo(GameObject room)
    {
        if (room == null || roomDebugInfoByRoom == null)
            return null;

        RoomDebugInfo info;
        roomDebugInfoByRoom.TryGetValue(room, out info);
        return info;
    }

    Color GetRoomBoundsGizmoColor(GameObject room)
    {
        RoomDebugInfo info = GetRoomDebugInfo(room);
        if (info != null)
        {
            if (info.isStart)
                return new Color(0.1f, 1f, 0.35f, 0.95f);
            if (info.isFinal || IsFinalRoom(room))
                return new Color(1f, 0.15f, 0.15f, 0.95f);

            return GetBranchGizmoColor(info.branchNumber);
        }

        RoomDefinition definition = GetRoomDefinition(room);
        if (definition == null)
            return Color.white;

        switch (definition.category)
        {
            case RoomCategory.SubmarineSpawn:
                return new Color(0.2f, 1f, 0.45f, 0.9f);

            case RoomCategory.Final:
                return new Color(1f, 0.25f, 0.25f, 0.9f);

            case RoomCategory.Special:
                return new Color(1f, 0.8f, 0.2f, 0.9f);

            case RoomCategory.Water:
            case RoomCategory.Pool:
                return new Color(0.15f, 0.55f, 1f, 0.9f);

            default:
                return new Color(0.7f, 0.9f, 1f, 0.9f);
        }
    }

    Color GetBranchGizmoColor(int branchNumber)
    {
        if (branchNumber <= 0)
            return new Color(0.65f, 0.9f, 1f, 0.9f);

        switch ((branchNumber - 1) % 8)
        {
            case 0:
                return new Color(0.15f, 0.85f, 1f, 0.95f);

            case 1:
                return new Color(1f, 0.75f, 0.15f, 0.95f);

            case 2:
                return new Color(0.45f, 1f, 0.35f, 0.95f);

            case 3:
                return new Color(1f, 0.35f, 0.7f, 0.95f);

            case 4:
                return new Color(0.7f, 0.55f, 1f, 0.95f);

            case 5:
                return new Color(0.95f, 0.55f, 0.25f, 0.95f);

            case 6:
                return new Color(0.25f, 1f, 0.75f, 0.95f);

            default:
                return new Color(0.95f, 0.95f, 0.35f, 0.95f);
        }
    }

    void StartMapSyncRegistration()
    {
        if (!synchronizeGeneratedMapToClients ||
            mapMessageHandlersRegistered ||
            !isActiveAndEnabled)
        {
            return;
        }

        if (mapSyncRegistrationCoroutine != null)
            return;

        mapSyncRegistrationCoroutine = StartCoroutine(RegisterMapSyncMessagingWhenReady());
    }

    IEnumerator RegisterMapSyncMessagingWhenReady()
    {
        while (isActiveAndEnabled)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null &&
                networkManager.IsListening &&
                networkManager.CustomMessagingManager != null)
            {
                RegisterMapSyncMessaging(networkManager);
                mapSyncRegistrationCoroutine = null;
                yield break;
            }

            yield return null;
        }

        mapSyncRegistrationCoroutine = null;
    }

    void RegisterMapSyncMessaging(NetworkManager networkManager)
    {
        if (networkManager == null || mapMessageHandlersRegistered)
            return;

        mapSyncNetworkManager = networkManager;
        mapSyncNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(
            GeneratedMapSnapshotMessageName,
            HandleGeneratedMapSnapshotMessage);
        mapSyncNetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(
            GeneratedMapRequestMessageName,
            HandleGeneratedMapRequestMessage);
        mapSyncNetworkManager.OnClientConnectedCallback += HandleMapSyncClientConnected;
        mapMessageHandlersRegistered = true;

        if (mapSyncNetworkManager.IsServer && generatedMapSnapshotReady)
        {
            SendGeneratedMapSnapshotToConnectedClients();
            FlushPendingRoomContentSpawns();
        }
        else if (mapSyncNetworkManager.IsClient && !mapSyncNetworkManager.IsServer)
            SendGeneratedMapRequest();
    }

    void UnregisterMapSyncMessaging()
    {
        if (mapSyncRegistrationCoroutine != null)
        {
            StopCoroutine(mapSyncRegistrationCoroutine);
            mapSyncRegistrationCoroutine = null;
        }

        if (!mapMessageHandlersRegistered || mapSyncNetworkManager == null)
            return;

        if (mapSyncNetworkManager.CustomMessagingManager != null)
        {
            mapSyncNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(
                GeneratedMapSnapshotMessageName);
            mapSyncNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(
                GeneratedMapRequestMessageName);
        }

        mapSyncNetworkManager.OnClientConnectedCallback -= HandleMapSyncClientConnected;
        mapMessageHandlersRegistered = false;
        mapSyncNetworkManager = null;
    }

    void HandleMapSyncClientConnected(ulong clientId)
    {
        if (!CanSendGeneratedMapSnapshot())
            return;
        if (clientId == NetworkManager.ServerClientId)
            return;

        SendGeneratedMapSnapshot(clientId);
    }

    void HandleGeneratedMapRequestMessage(
        ulong senderClientId,
        FastBufferReader messagePayload)
    {
        if (!CanSendGeneratedMapSnapshot())
            return;
        if (senderClientId == NetworkManager.ServerClientId)
            return;

        SendGeneratedMapSnapshot(senderClientId);
    }

    void HandleGeneratedMapSnapshotMessage(
        ulong senderClientId,
        FastBufferReader messagePayload)
    {
        if (!synchronizeGeneratedMapToClients ||
            mapSyncNetworkManager == null ||
            mapSyncNetworkManager.IsServer ||
            senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        int receivedSeed;
        bool receivedMapConsolidated;
        int roomCount;
        messagePayload.ReadValueSafe(out receivedSeed);
        messagePayload.ReadValueSafe(out receivedMapConsolidated);
        messagePayload.ReadValueSafe(out roomCount);

        List<GeneratedMapRoomSnapshot> roomSnapshots =
            new List<GeneratedMapRoomSnapshot>(Mathf.Max(0, roomCount));

        for (int i = 0; i < roomCount; i++)
            roomSnapshots.Add(ReadGeneratedMapRoomSnapshot(ref messagePayload));

        ApplyGeneratedMapSnapshot(
            receivedSeed,
            receivedMapConsolidated,
            roomSnapshots);
    }

    void NotifyGeneratedMapSnapshotReady()
    {
        generatedMapSnapshotReady = true;

        if (synchronizeGeneratedMapToClients)
            StartMapSyncRegistration();

        if (CanSendGeneratedMapSnapshot())
        {
            SendGeneratedMapSnapshotToConnectedClients();
            FlushPendingRoomContentSpawns();
        }
        else
        {
            StartRoomContentFlushWhenReady();
        }
    }

    bool CanSendGeneratedMapSnapshot()
    {
        return synchronizeGeneratedMapToClients &&
            generatedMapSnapshotReady &&
            mapSyncNetworkManager != null &&
            mapSyncNetworkManager.IsListening &&
            mapSyncNetworkManager.IsServer &&
            mapSyncNetworkManager.CustomMessagingManager != null;
    }

    void SendGeneratedMapRequest()
    {
        if (mapSyncNetworkManager == null ||
            mapSyncNetworkManager.CustomMessagingManager == null ||
            !mapSyncNetworkManager.IsClient ||
            mapSyncNetworkManager.IsServer)
        {
            return;
        }

        FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp);
        try
        {
            mapSyncNetworkManager.CustomMessagingManager.SendNamedMessage(
                GeneratedMapRequestMessageName,
                NetworkManager.ServerClientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }
        finally
        {
            writer.Dispose();
        }
    }

    void SendGeneratedMapSnapshotToConnectedClients()
    {
        if (!CanSendGeneratedMapSnapshot())
            return;

        for (int i = 0; i < mapSyncNetworkManager.ConnectedClientsIds.Count; i++)
        {
            ulong clientId = mapSyncNetworkManager.ConnectedClientsIds[i];
            if (clientId == NetworkManager.ServerClientId)
                continue;

            SendGeneratedMapSnapshot(clientId);
        }
    }

    void SendGeneratedMapSnapshot(ulong clientId)
    {
        if (!CanSendGeneratedMapSnapshot())
            return;

        List<GeneratedMapRoomSnapshot> roomSnapshots =
            BuildGeneratedMapSnapshot();
        int writerSize = CalculateGeneratedMapSnapshotWriteSize(roomSnapshots);
        FastBufferWriter writer = new FastBufferWriter(
            writerSize,
            Allocator.Temp,
            int.MaxValue);

        try
        {
            writer.WriteValueSafe(seed);
            writer.WriteValueSafe(mapConsolidated);
            writer.WriteValueSafe(roomSnapshots.Count);

            for (int i = 0; i < roomSnapshots.Count; i++)
                WriteGeneratedMapRoomSnapshot(ref writer, roomSnapshots[i]);

            mapSyncNetworkManager.CustomMessagingManager.SendNamedMessage(
                GeneratedMapSnapshotMessageName,
                clientId,
                writer,
                NetworkDelivery.ReliableFragmentedSequenced);
        }
        finally
        {
            writer.Dispose();
        }
    }

    List<GeneratedMapRoomSnapshot> BuildGeneratedMapSnapshot()
    {
        List<GeneratedMapRoomSnapshot> roomSnapshots =
            new List<GeneratedMapRoomSnapshot>(spawnedRooms.Count);

        for (int i = 0; i < spawnedRooms.Count; i++)
        {
            GameObject room = spawnedRooms[i];
            if (room == null)
                continue;

            int prefabIndex;
            if (!generatedPrefabIndicesByRoom.TryGetValue(room, out prefabIndex))
                prefabIndex = -1;

            if (prefabIndex < 0 ||
                roomPrefabs == null ||
                prefabIndex >= roomPrefabs.Length ||
                roomPrefabs[prefabIndex] == null)
            {
                Debug.LogWarning(
                    $"RoomGenerator skipped map sync for {room.name} because its prefab index is invalid.");
                continue;
            }

            RoomPlacement placement;
            placementsByRoom.TryGetValue(room, out placement);

            roomSnapshots.Add(new GeneratedMapRoomSnapshot
            {
                roomIndex = i,
                prefabIndex = prefabIndex,
                position = room.transform.position,
                rotation = room.transform.rotation,
                cell = placement != null ? placement.cell : Vector2Int.zero,
                connectorStates = GetConnectorStateSnapshot(room)
            });
        }

        return roomSnapshots;
    }

    int[] GetConnectorStateSnapshot(GameObject room)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        if (definition == null || definition.connectors == null)
            return new int[0];

        int[] connectorStates = new int[definition.connectors.Length];
        for (int i = 0; i < definition.connectors.Length; i++)
        {
            RoomConnector connector = definition.connectors[i];
            connectorStates[i] = connector != null
                ? (int)connector.State
                : (int)RoomConnectorState.Open;
        }

        return connectorStates;
    }

    void WriteGeneratedMapRoomSnapshot(
        ref FastBufferWriter writer,
        GeneratedMapRoomSnapshot roomSnapshot)
    {
        writer.WriteValueSafe(roomSnapshot.roomIndex);
        writer.WriteValueSafe(roomSnapshot.prefabIndex);
        WriteVector3(ref writer, roomSnapshot.position);
        WriteQuaternion(ref writer, roomSnapshot.rotation);
        writer.WriteValueSafe(roomSnapshot.cell.x);
        writer.WriteValueSafe(roomSnapshot.cell.y);

        int connectorCount = roomSnapshot.connectorStates != null
            ? roomSnapshot.connectorStates.Length
            : 0;
        writer.WriteValueSafe(connectorCount);

        for (int i = 0; i < connectorCount; i++)
            writer.WriteValueSafe(roomSnapshot.connectorStates[i]);
    }

    GeneratedMapRoomSnapshot ReadGeneratedMapRoomSnapshot(
        ref FastBufferReader reader)
    {
        GeneratedMapRoomSnapshot roomSnapshot = new GeneratedMapRoomSnapshot();
        int cellX;
        int cellY;
        int connectorCount;

        reader.ReadValueSafe(out roomSnapshot.roomIndex);
        reader.ReadValueSafe(out roomSnapshot.prefabIndex);
        roomSnapshot.position = ReadVector3(ref reader);
        roomSnapshot.rotation = ReadQuaternion(ref reader);
        reader.ReadValueSafe(out cellX);
        reader.ReadValueSafe(out cellY);
        reader.ReadValueSafe(out connectorCount);

        roomSnapshot.cell = new Vector2Int(cellX, cellY);
        connectorCount = Mathf.Max(0, connectorCount);
        roomSnapshot.connectorStates = new int[connectorCount];

        for (int i = 0; i < connectorCount; i++)
            reader.ReadValueSafe(out roomSnapshot.connectorStates[i]);

        return roomSnapshot;
    }

    int CalculateGeneratedMapSnapshotWriteSize(
        List<GeneratedMapRoomSnapshot> roomSnapshots)
    {
        int size = 64;
        if (roomSnapshots == null)
            return size;

        for (int i = 0; i < roomSnapshots.Count; i++)
        {
            int connectorCount = roomSnapshots[i].connectorStates != null
                ? roomSnapshots[i].connectorStates.Length
                : 0;
            size += 96 + connectorCount * 4;
        }

        return Mathf.Max(256, size);
    }

    void ApplyGeneratedMapSnapshot(
        int receivedSeed,
        bool receivedMapConsolidated,
        List<GeneratedMapRoomSnapshot> roomSnapshots)
    {
        ClearGeneratedMapForRetry();

        seed = receivedSeed;
        mapConsolidated = receivedMapConsolidated;

        if (roomSnapshots == null)
            roomSnapshots = new List<GeneratedMapRoomSnapshot>();

        for (int i = 0; i < roomSnapshots.Count; i++)
            InstantiateSynchronizedRoom(roomSnapshots[i]);

        generatedRoomCount = spawnedRooms.Count;
        generatedMapSnapshotReady = true;
        Physics.SyncTransforms();

        if (ShouldTeleportClientPlayerAfterMapSync())
            TeleportLocalClientPlayerToSpawn();

        Debug.Log($"RoomGenerator synchronized {spawnedRooms.Count} room(s) from host.");
    }

    void InstantiateSynchronizedRoom(GeneratedMapRoomSnapshot roomSnapshot)
    {
        if (roomPrefabs == null ||
            roomSnapshot.prefabIndex < 0 ||
            roomSnapshot.prefabIndex >= roomPrefabs.Length)
        {
            Debug.LogWarning(
                $"RoomGenerator received invalid room prefab index {roomSnapshot.prefabIndex}.");
            return;
        }

        GameObject roomPrefab = roomPrefabs[roomSnapshot.prefabIndex];
        if (roomPrefab == null)
            return;

        GameObject room = Instantiate(
            roomPrefab,
            roomSnapshot.position,
            roomSnapshot.rotation);
        room.name = $"{roomPrefab.name}_Synced_{roomSnapshot.roomIndex:00}";

        ApplySynchronizedConnectorStates(room, roomSnapshot.connectorStates);

        RoomPlacement placement = new RoomPlacement
        {
            room = room,
            cell = roomSnapshot.cell,
            bounds = CalculateRoomBounds(room)
        };

        spawnedRooms.Add(room);
        RegisterRoomPlacement(placement);
        generatedPrefabIndicesByRoom[room] = roomSnapshot.prefabIndex;
        TrackGeneratedPrefab(roomPrefab, roomSnapshot.roomIndex);
        RecordRoomDebugInfo(room, placement, roomSnapshot.roomIndex);
        RegisterRoomDoors(room);
        RegisterRoomNavMesh(room);
    }

    void ApplySynchronizedConnectorStates(
        GameObject room,
        int[] connectorStates)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        if (definition == null || definition.connectors == null)
            return;

        for (int i = 0; i < definition.connectors.Length; i++)
        {
            RoomConnector connector = definition.connectors[i];
            if (connector == null)
                continue;

            RoomConnectorState synchronizedState = RoomConnectorState.Open;
            if (connectorStates != null && i < connectorStates.Length)
                synchronizedState = (RoomConnectorState)connectorStates[i];

            connector.ApplySynchronizedState(synchronizedState);
        }
    }

    void TeleportLocalClientPlayerToSpawn()
    {
        if (mapSyncNetworkManager == null || mapSyncNetworkManager.IsServer)
            return;

        NetworkObject localPlayerObject = mapSyncNetworkManager.SpawnManager != null
            ? mapSyncNetworkManager.SpawnManager.GetLocalPlayerObject()
            : null;
        if (localPlayerObject == null)
            return;

        Transform spawn = FindPlayerSpawn();
        if (spawn == null)
            return;

        Rigidbody body = localPlayerObject.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        localPlayerObject.transform.SetPositionAndRotation(
            spawn.position,
            spawn.rotation);

        clientPlayerTeleportedAfterInitialMapSync = true;
    }

    bool ShouldTeleportClientPlayerAfterMapSync()
    {
        return teleportClientPlayerAfterMapSync &&
            !clientPlayerTeleportedAfterInitialMapSync;
    }

    Transform FindPlayerSpawn()
    {
        if (string.IsNullOrWhiteSpace(playerSpawnName))
            return null;

        GameObject spawnObject = GameObject.Find(playerSpawnName);
        return spawnObject != null ? spawnObject.transform : null;
    }

    void WriteVector3(ref FastBufferWriter writer, Vector3 value)
    {
        writer.WriteValueSafe(value.x);
        writer.WriteValueSafe(value.y);
        writer.WriteValueSafe(value.z);
    }

    Vector3 ReadVector3(ref FastBufferReader reader)
    {
        float x;
        float y;
        float z;
        reader.ReadValueSafe(out x);
        reader.ReadValueSafe(out y);
        reader.ReadValueSafe(out z);
        return new Vector3(x, y, z);
    }

    void WriteQuaternion(ref FastBufferWriter writer, Quaternion value)
    {
        writer.WriteValueSafe(value.x);
        writer.WriteValueSafe(value.y);
        writer.WriteValueSafe(value.z);
        writer.WriteValueSafe(value.w);
    }

    Quaternion ReadQuaternion(ref FastBufferReader reader)
    {
        float x;
        float y;
        float z;
        float w;
        reader.ReadValueSafe(out x);
        reader.ReadValueSafe(out y);
        reader.ReadValueSafe(out z);
        reader.ReadValueSafe(out w);
        return new Quaternion(x, y, z, w);
    }

    int GetRoomPrefabIndex(GameObject roomPrefab)
    {
        if (roomPrefab == null || roomPrefabs == null)
            return -1;

        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            if (roomPrefabs[i] == roomPrefab)
                return i;
        }

        return -1;
    }

    void CullOldRooms()
    {
        if (keepGeneratedRoomsForBacktracking) return;
        if (roomsToKeep <= 0) return;

        while (spawnedRooms.Count > roomsToKeep)
        {
            GameObject room = spawnedRooms[0];
            spawnedRooms.RemoveAt(0);
            RemoveOpenConnectorsForRoom(room);
            roomDebugInfoByRoom.Remove(room);
            RemoveDebugBranchConnectionsForRoom(room);

            NavMeshSurface surface = room != null ? room.GetComponent<NavMeshSurface>() : null;
            if (surface == null && room != null)
                surface = room.GetComponentInChildren<NavMeshSurface>(true);

            if (surface != null && EnemySpawner.Instance != null)
                EnemySpawner.Instance.UnregisterSurface(surface);

            if (resourceSpawner != null)
                resourceSpawner.DespawnResourcesForRoom(room);

            if (enemySpawner != null)
                enemySpawner.DespawnEnemiesForRoom(room);

            if (room != null)
                Destroy(room);
        }
    }

    void RemoveDebugBranchConnectionsForRoom(GameObject room)
    {
        if (room == null)
            return;

        for (int i = debugBranchConnections.Count - 1; i >= 0; i--)
        {
            ConnectorDebugConnection connection = debugBranchConnections[i];
            if (connection == null ||
                ConnectorBelongsToRoom(connection.first, room) ||
                ConnectorBelongsToRoom(connection.second, room))
            {
                debugBranchConnections.RemoveAt(i);
            }
        }
    }

    bool ConnectorBelongsToRoom(RoomConnector connector, GameObject room)
    {
        if (connector == null || room == null)
            return false;

        return connector.transform.IsChildOf(room.transform);
    }
}
