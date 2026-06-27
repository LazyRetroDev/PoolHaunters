using UnityEngine;
using Unity.AI.Navigation;
using System.Collections.Generic;

public class RoomGenerator : MonoBehaviour
{
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

    private readonly List<GameObject> spawnedRooms = new List<GameObject>();
    private readonly List<NavMeshSurface> registeredSurfaces = new List<NavMeshSurface>();
    private readonly List<RoomConnector> openConnectors = new List<RoomConnector>();
    private readonly Dictionary<GameObject, int> generatedPrefabCounts =
        new Dictionary<GameObject, int>();

    private Transform lastExitPoint;
    private int generatedRoomCount;
    private bool initialEnemySpawned;

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
        if (roomPrefabs == null || roomPrefabs.Length == 0)
        {
            Debug.LogWarning("RoomGenerator has no room prefabs assigned.");
            return null;
        }

        if (maxGeneratedRooms > 0 && generatedRoomCount >= maxGeneratedRooms)
            return null;

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

        GameObject roomPrefab = ChooseRoomPrefab(expansionConnector);
        if (roomPrefab == null)
        {
            Debug.LogWarning("RoomGenerator has no available room prefab for the current rules.");
            return null;
        }

        GameObject room = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity);

        if (generatedRoomCount > 0 && !AlignRoomToExpansion(room, expansionConnector, expansionPoint))
        {
            Destroy(room);
            return null;
        }

        int roomIndex = generatedRoomCount;
        spawnedRooms.Add(room);
        TrackGeneratedPrefab(roomPrefab);
        generatedRoomCount++;

        if (resourceSpawner != null)
            resourceSpawner.SpawnResourcesForRoom(room, roomIndex, seed);

        RegisterRoomDoors(room);
        RegisterRoomNavMesh(room);
        AddOpenConnectors(room);
        UpdateLegacyExitPoint(room);

        TrySpawnInitialTimeCamper();

        return room;
    }

    GameObject ChooseRoomPrefab(RoomConnector expansionConnector)
    {
        GameObject prefab = ChooseRoomPrefab(expansionConnector, useProgressionFilter: true);
        if (prefab != null)
            return prefab;

        if (progression != null && progression.ShouldFallbackWhenNoPrefabMatches)
        {
            Debug.LogWarning(
                $"RoomGenerator found no room for progression rule '{progression.GetRuleLabel(generatedRoomCount, maxGeneratedRooms)}' at index {generatedRoomCount}. Falling back to any compatible category.");
            return ChooseRoomPrefab(expansionConnector, useProgressionFilter: false);
        }

        return null;
    }

    GameObject ChooseRoomPrefab(RoomConnector expansionConnector, bool useProgressionFilter)
    {
        float totalWeight = 0f;

        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            GameObject prefab = roomPrefabs[i];
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
            if (!CanSpawnRoomPrefab(prefab, useProgressionFilter)) continue;
            if (!CanRoomPrefabConnectTo(prefab, expansionConnector)) continue;

            roll -= GetRoomPrefabWeight(prefab);
            if (roll <= 0f)
                return prefab;
        }

        return null;
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

        if (!definition.CanSpawn(GetGeneratedPrefabCount(prefab)))
            return false;

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

    void TrackGeneratedPrefab(GameObject prefab)
    {
        if (prefab == null) return;
        generatedPrefabCounts[prefab] = GetGeneratedPrefabCount(prefab) + 1;
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

        if (expansionConnector != null && entryConnector != null)
        {
            if (!ConnectRooms(expansionConnector, entryConnector))
                return false;
        }
        else if (expansionConnector != null)
        {
            expansionConnector.Close();
        }

        CleanOpenConnectors();
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
        return true;
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

            openConnectors.Add(connector);
        }
    }

    void CleanOpenConnectors()
    {
        openConnectors.RemoveAll(connector =>
            connector == null || !connector.canBeExit || !connector.IsAvailable);
    }

    void RemoveOpenConnectorsForRoom(GameObject room)
    {
        if (room == null) return;

        RoomDefinition definition = GetRoomDefinition(room);
        if (definition != null && definition.connectors != null)
        {
            for (int i = 0; i < definition.connectors.Length; i++)
                openConnectors.Remove(definition.connectors[i]);
        }

        if (lastExitPoint != null && lastExitPoint.IsChildOf(room.transform))
            lastExitPoint = null;
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
