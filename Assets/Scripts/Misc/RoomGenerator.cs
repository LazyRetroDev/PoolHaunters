using UnityEngine;
using Unity.AI.Navigation;
using System.Collections.Generic;

public class RoomGenerator : MonoBehaviour
{
    class RoomPlacement
    {
        public GameObject room;
        public Vector2Int cell;
        public Bounds bounds;
    }

    [Header("Rooms")]
    public GameObject[] roomPrefabs;
    public int startingRoomCount = 2;
    public int maxGeneratedRooms = 0;

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

        if (maxGeneratedRooms > 0 && generatedRoomCount >= maxGeneratedRooms)
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

            TrySpawnInitialTimeCamper();

            if (ShouldConsolidateAfterRoom(room))
                ConsolidateGeneratedMap();

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

        Vector3 center = (exitPoint.position + entryPoint.position) * 0.5f;
        Vector3 up = exitPoint.up;
        center += up * (doorwayClearanceHeight * 0.5f);

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
        CreateNavMeshLink(exitConnector, entryConnector);
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

        registeredSurfaces.Add(surface);
        EnemySpawner.Instance.RegisterSurface(surface, buildNow: true);
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
