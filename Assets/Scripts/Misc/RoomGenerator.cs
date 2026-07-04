using UnityEngine;
using Unity.AI.Navigation;
using System.Collections;
using System.Collections.Generic;

public class RoomGenerator : MonoBehaviour
{
    const string ClosedDoorChildName = "ClosedDoor";

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
        public bool mapConsolidated;
        public Transform lastExitPoint;
        public List<RoomConnector> openConnectors;
        public Dictionary<GameObject, int> generatedPrefabCounts;
        public Dictionary<GameObject, int> lastGeneratedPrefabIndices;
        public Dictionary<GameObject, RoomPlacement> placementsByRoom;
        public Dictionary<Vector2Int, RoomPlacement> placementsByCell;
        public Dictionary<RoomConnector, Vector2Int> connectorTargetCells;
        public Dictionary<RoomConnector, NavMeshLink> navMeshLinksByConnector;
    }

    class FullMapGenerationStats
    {
        public int requiredBranchCount;
        public int requestedBranchCount;
        public int completedBranchCount;
        public int branchBuildAttempts;
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

    [Header("Rooms")]
    public GameObject[] roomPrefabs;
    public int startingRoomCount = 2;
    public int maxGeneratedRooms = 0;

    [Header("Full Map Generation")]
    public bool generateFullMapOnStart;

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

    [Header("Full Map Validation")]
    public bool validateFullMapAfterGeneration = true;

    [Min(1)]
    public int fullMapGenerationAttempts = 20;

    public bool validateClosedDoorObjects = true;
    public bool validateConnectedNavMeshLinks = true;

    [Tooltip("Keep enabled while the player can walk back through already generated rooms.")]
    public bool keepGeneratedRoomsForBacktracking = true;

    [Tooltip("Only used when backtracking preservation is disabled. Use 0 to keep every spawned room.")]
    [Min(0)]
    public int roomsToKeep = 0;

    public int seed = 0;

    [Header("Run Seed")]
    public bool useSelectedRunSeed = true;
    public bool randomizeSeedWhenNoRunSelected = false;

    [Header("Enemy Setup")]
    public bool spawnTimeCamperAfterStartingRooms = true;

    [Header("Room Resources")]
    [SerializeField] private RoomResourceSpawner resourceSpawner;

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
    private readonly Dictionary<GameObject, RoomPlacement> placementsByRoom =
        new Dictionary<GameObject, RoomPlacement>();
    private readonly Dictionary<Vector2Int, RoomPlacement> placementsByCell =
        new Dictionary<Vector2Int, RoomPlacement>();
    private readonly Dictionary<RoomConnector, Vector2Int> connectorTargetCells =
        new Dictionary<RoomConnector, Vector2Int>();
    private readonly Dictionary<RoomConnector, NavMeshLink> navMeshLinksByConnector =
        new Dictionary<RoomConnector, NavMeshLink>();

    private Transform navMeshLinkRoot;
    private Transform lastExitPoint;
    private int generatedRoomCount;
    private bool initialEnemySpawned;
    private bool mapConsolidated;
    private bool isGeneratingFullMap;

    void Awake()
    {
        if (resourceSpawner == null)
            resourceSpawner = GetComponent<RoomResourceSpawner>();

        if (progression == null)
            progression = GetComponent<RoomProgressionController>();
    }

    void Start()
    {
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
        GenerateNextRoom(trigger);
        CullOldRooms();
    }

    public GameObject GenerateNextRoom()
    {
        return GenerateNextRoom(null);
    }

    GameObject GenerateNextRoom(DoorTrigger trigger)
    {
        if (mapConsolidated)
            return null;

        if (roomPrefabs == null || roomPrefabs.Length == 0)
        {
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
            Debug.LogWarning("RoomGenerator has no open connector available to expand from.");
            return null;
        }

        Vector2Int targetCell;
        if (!TryGetRoomTargetCell(expansionConnector, out targetCell))
        {
            Debug.LogWarning("RoomGenerator could not calculate a target cell for the selected connector.");
            CloseBlockedConnector(expansionConnector);
            return null;
        }

        if (IsGridCellOccupied(targetCell))
        {
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
                Debug.LogWarning("RoomGenerator has no available room prefab for the current rules.");
                break;
            }

            GameObject room = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity);

            if (generatedRoomCount > 0 && !AlignRoomToExpansion(room, expansionConnector, expansionPoint))
            {
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                continue;
            }

            if (generatedRoomCount > 0 &&
                !HasClearConnectedDoorway(room, expansionConnector))
            {
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
                Debug.LogWarning($"RoomGenerator rejected {room.name}: {rejectionReason}");
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                blockedByPlacement = true;
                continue;
            }

            if (generatedRoomCount > 0 && !FinalizeRoomConnection(room, expansionConnector))
            {
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
        TrackGeneratedPrefab(roomPrefab, roomIndex);
        generatedRoomCount++;

        if (resourceSpawner != null)
            resourceSpawner.SpawnResourcesForRoom(room, roomIndex, seed);

        RegisterRoomDoors(room);
        RegisterRoomNavMesh(room);
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

            if (!validateFullMapAfterGeneration || lastValidation.IsValid)
            {
                if (attempt > 0)
                {
                    Debug.Log(
                        $"RoomGenerator accepted full map attempt {attempt + 1}/{attempts} with seed {seed}.");
                }

                TrySpawnInitialTimeCamper();
                return;
            }

            Debug.LogWarning(
                $"RoomGenerator rejected full map attempt {attempt + 1}/{attempts} with seed {seed}: {lastValidation.GetSummary()}");
        }

        Debug.LogError(
            $"RoomGenerator could not produce a valid full map after {attempts} attempt(s). Keeping last generated map for inspection. Last validation: {(lastValidation != null ? lastValidation.GetSummary() : "no validation result")}");
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

        stats.requestedBranchCount = Mathf.Max(
            stats.requiredBranchCount,
            GetRandomConfiguredValue(
                minimumBranchCount,
                maximumBranchCount));

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

            if (GenerateBranch(
                branchStart,
                branchRoomCount,
                futureBranchStartsNeeded))
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

        ValidateRoomPlacements(result);
        ValidateConnectorStates(result);

        return result;
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

        if (navMeshLinkRoot != null)
        {
            DestroyGeneratedObject(navMeshLinkRoot.gameObject);
            navMeshLinkRoot = null;
        }

        for (int i = 0; i < spawnedRooms.Count; i++)
            DestroyGeneratedObject(spawnedRooms[i]);

        spawnedRooms.Clear();
        openConnectors.Clear();
        generatedPrefabCounts.Clear();
        lastGeneratedPrefabIndices.Clear();
        placementsByRoom.Clear();
        placementsByCell.Clear();
        connectorTargetCells.Clear();
        navMeshLinksByConnector.Clear();
        lastExitPoint = null;
        generatedRoomCount = 0;
        initialEnemySpawned = false;
        mapConsolidated = false;

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
            mapConsolidated = mapConsolidated,
            lastExitPoint = lastExitPoint,
            openConnectors = new List<RoomConnector>(openConnectors),
            generatedPrefabCounts = new Dictionary<GameObject, int>(generatedPrefabCounts),
            lastGeneratedPrefabIndices = new Dictionary<GameObject, int>(lastGeneratedPrefabIndices),
            placementsByRoom = new Dictionary<GameObject, RoomPlacement>(placementsByRoom),
            placementsByCell = new Dictionary<Vector2Int, RoomPlacement>(placementsByCell),
            connectorTargetCells = new Dictionary<RoomConnector, Vector2Int>(connectorTargetCells),
            navMeshLinksByConnector = new Dictionary<RoomConnector, NavMeshLink>(navMeshLinksByConnector)
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
        mapConsolidated = snapshot.mapConsolidated;
        lastExitPoint = snapshot.lastExitPoint;

        RestoreList(openConnectors, snapshot.openConnectors);
        RestoreDictionary(generatedPrefabCounts, snapshot.generatedPrefabCounts);
        RestoreDictionary(lastGeneratedPrefabIndices, snapshot.lastGeneratedPrefabIndices);
        RestoreDictionary(placementsByRoom, snapshot.placementsByRoom);
        RestoreDictionary(placementsByCell, snapshot.placementsByCell);
        RestoreDictionary(connectorTargetCells, snapshot.connectorTargetCells);
        RestoreDictionary(navMeshLinksByConnector, snapshot.navMeshLinksByConnector);

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
            return null;

        if (expansionConnector == null || !expansionConnector.IsAvailable)
            return null;

        Transform expansionPoint = expansionConnector.Point;
        if (expansionPoint == null)
        {
            CloseBlockedConnector(expansionConnector);
            return null;
        }

        Vector2Int targetCell;
        if (!TryGetRoomTargetCell(expansionConnector, out targetCell))
        {
            CloseBlockedConnector(expansionConnector);
            return null;
        }

        if (IsGridCellOccupied(targetCell))
        {
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
                break;

            GameObject room = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity);

            if (!AlignRoomToExpansion(room, expansionConnector, expansionPoint))
            {
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                continue;
            }

            if (!HasClearConnectedDoorway(room, expansionConnector))
            {
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
                Debug.LogWarning(
                    $"RoomGenerator rejected {room.name}: only {openExitCount} free exit(s), but {Mathf.Max(1, minimumOpenExits)} are needed to keep the branch plan valid.");
                Destroy(room);
                rejectedPrefabs.Add(roomPrefab);
                continue;
            }

            if (!FinalizeRoomConnection(room, expansionConnector))
            {
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

    void TrySpawnInitialTimeCamper()
    {
        if (initialEnemySpawned) return;
        if (!spawnTimeCamperAfterStartingRooms) return;
        if (spawnedRooms.Count < startingRoomCount) return;
        if (EnemySpawner.Instance == null) return;

        EnemySpawner.Instance.SpawnTimeCamper();
        initialEnemySpawned = true;
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

            return false;
        }

        return true;
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
        connectorTargetCells.Remove(exitConnector);
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

            if (IsGridCellOccupied(targetCell))
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

            if (IsConnectorTargetCellOccupied(connector))
                CloseBlockedConnector(connector);
        }
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
            surface = room.GetComponentInChildren<NavMeshSurface>();

        if (surface == null)
        {
            Debug.LogWarning(room.name + " is missing a NavMeshSurface.");
            return;
        }

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

    void CullOldRooms()
    {
        if (keepGeneratedRoomsForBacktracking) return;
        if (roomsToKeep <= 0) return;

        while (spawnedRooms.Count > roomsToKeep)
        {
            GameObject room = spawnedRooms[0];
            spawnedRooms.RemoveAt(0);
            RemoveOpenConnectorsForRoom(room);

            NavMeshSurface surface = room != null ? room.GetComponent<NavMeshSurface>() : null;
            if (surface == null && room != null)
                surface = room.GetComponentInChildren<NavMeshSurface>();

            if (surface != null && EnemySpawner.Instance != null)
                EnemySpawner.Instance.UnregisterSurface(surface);

            if (resourceSpawner != null)
                resourceSpawner.DespawnResourcesForRoom(room);

            if (room != null)
                Destroy(room);
        }
    }
}
