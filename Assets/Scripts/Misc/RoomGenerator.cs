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

        GameObject roomPrefab = roomPrefabs[Random.Range(0, roomPrefabs.Length)];
        GameObject room = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity);

        AlignRoomToPreviousExit(room);

        int roomIndex = generatedRoomCount;
        spawnedRooms.Add(room);
        generatedRoomCount++;

        if (resourceSpawner != null)
            resourceSpawner.SpawnResourcesForRoom(room, roomIndex, seed);

        RegisterRoomDoor(room);
        RegisterRoomNavMesh(room);

        Transform exitPoint = room.transform.Find("DoorPoint_B");
        if (exitPoint != null)
            lastExitPoint = exitPoint;
        else
            Debug.LogWarning(room.name + " is missing DoorPoint_B.");

        TrySpawnInitialTimeCamper();

        return room;
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

        Transform entryPoint = room.transform.Find("DoorPoint_A");
        if (entryPoint == null)
        {
            Debug.LogWarning(room.name + " is missing DoorPoint_A.");
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
        DoorTrigger exitTrigger = room.transform.Find("DoorTrigger_B")?.GetComponent<DoorTrigger>();
        if (exitTrigger != null)
            exitTrigger.Initialize(this);
        else
            Debug.LogWarning(room.name + " is missing DoorTrigger_B with DoorTrigger.");
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
