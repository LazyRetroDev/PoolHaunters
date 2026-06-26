using UnityEngine;
using Unity.AI.Navigation;
using System.Collections.Generic;

public class RoomGenerator : MonoBehaviour
{
    [Header("Rooms")]
    public GameObject[] roomPrefabs;
    public int startingRoomCount = 2;
    public int maxGeneratedRooms = 0;
    public int roomsToKeep = 5;
    public int seed = 0;

    [Header("Run Seed")]
    public bool useSelectedRunSeed = true;
    public bool randomizeSeedWhenNoRunSelected = false;

    [Header("Enemy Setup")]
    public bool spawnTimeCamperAfterStartingRooms = true;

    [Header("Room Resources")]
    [SerializeField] private RoomResourceSpawner resourceSpawner;

    private readonly List<GameObject> spawnedRooms = new List<GameObject>();
    private readonly List<NavMeshSurface> registeredSurfaces = new List<NavMeshSurface>();
    private readonly Dictionary<GameObject, int> generatedPrefabCounts =
        new Dictionary<GameObject, int>();

    private Transform lastExitPoint;
    private int generatedRoomCount;
    private bool initialEnemySpawned;

    void Awake()
    {
        if (resourceSpawner == null)
            resourceSpawner = GetComponent<RoomResourceSpawner>();
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
        GenerateNextRoom();
        CullOldRooms();
    }

    public GameObject GenerateNextRoom()
    {
        if (roomPrefabs == null || roomPrefabs.Length == 0)
        {
            Debug.LogWarning("RoomGenerator has no room prefabs assigned.");
            return null;
        }

        if (maxGeneratedRooms > 0 && generatedRoomCount >= maxGeneratedRooms)
            return null;

        GameObject roomPrefab = ChooseRoomPrefab();
        if (roomPrefab == null)
        {
            Debug.LogWarning("RoomGenerator has no available room prefab for the current rules.");
            return null;
        }

        GameObject room = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity);

        AlignRoomToPreviousExit(room);

        int roomIndex = generatedRoomCount;
        spawnedRooms.Add(room);
        TrackGeneratedPrefab(roomPrefab);
        generatedRoomCount++;

        if (resourceSpawner != null)
            resourceSpawner.SpawnResourcesForRoom(room, roomIndex, seed);

        RegisterRoomDoor(room);
        RegisterRoomNavMesh(room);

        Transform exitPoint = GetRoomExitPoint(room);
        if (exitPoint != null)
            lastExitPoint = exitPoint;
        else
            Debug.LogWarning(room.name + " is missing an exit point. Add a RoomDefinition exit door or DoorPoint_B.");

        TrySpawnInitialTimeCamper();

        return room;
    }

    GameObject ChooseRoomPrefab()
    {
        float totalWeight = 0f;

        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            GameObject prefab = roomPrefabs[i];
            if (!CanSpawnRoomPrefab(prefab)) continue;
            totalWeight += GetRoomPrefabWeight(prefab);
        }

        if (totalWeight <= 0f)
            return null;

        float roll = Random.Range(0f, totalWeight);
        for (int i = 0; i < roomPrefabs.Length; i++)
        {
            GameObject prefab = roomPrefabs[i];
            if (!CanSpawnRoomPrefab(prefab)) continue;

            roll -= GetRoomPrefabWeight(prefab);
            if (roll <= 0f)
                return prefab;
        }

        return null;
    }

    bool CanSpawnRoomPrefab(GameObject prefab)
    {
        if (prefab == null) return false;

        RoomDefinition definition = GetRoomDefinition(prefab);
        if (definition == null)
            return true;

        return definition.CanSpawn(GetGeneratedPrefabCount(prefab));
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

    void AlignRoomToPreviousExit(GameObject room)
    {
        if (lastExitPoint == null) return;

        Transform entryPoint = GetRoomEntrancePoint(room);
        if (entryPoint == null)
        {
            Debug.LogWarning(room.name + " is missing an entrance point. Add a RoomDefinition entrance door or DoorPoint_A.");
            return;
        }

        Quaternion rotationDiff = lastExitPoint.rotation * Quaternion.Inverse(entryPoint.rotation);
        rotationDiff *= Quaternion.Euler(0f, 180f, 0f);
        room.transform.rotation = rotationDiff;

        Vector3 offset = lastExitPoint.position - entryPoint.position;
        room.transform.position += offset;
    }

    void RegisterRoomDoor(GameObject room)
    {
        DoorTrigger exitTrigger = GetRoomExitTrigger(room);
        if (exitTrigger != null)
            exitTrigger.Initialize(this);
        else
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

    Transform GetRoomExitPoint(GameObject room)
    {
        RoomDefinition definition = GetRoomDefinition(room);
        Transform point;
        if (definition != null && definition.TryGetExitPoint(out point))
            return point;

        return room.transform.Find("DoorPoint_B");
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
        if (roomsToKeep <= 0) return;

        while (spawnedRooms.Count > roomsToKeep)
        {
            GameObject room = spawnedRooms[0];
            spawnedRooms.RemoveAt(0);

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
