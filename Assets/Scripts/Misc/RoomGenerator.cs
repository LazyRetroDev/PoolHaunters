using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections.Generic;

public class RoomGenerator : MonoBehaviour
{
    public GameObject[] roomPrefabs;
    public int roomCount = 3;
    public int seed = 0;

    private List<GameObject> spawnedRooms = new List<GameObject>();

    void Start()
    {
        Random.InitState(seed);
<<<<<<< Updated upstream
        GenerateRooms();
        BakeAndSpawnEnemies();
=======

        int roomsToGenerate = Mathf.Max(1, startingRoomCount);
        for (int i = 0; i < roomsToGenerate; i++)
            GenerateNextRoom();
>>>>>>> Stashed changes
    }

    void GenerateRooms()
    {
        Transform lastExitPoint = null;
        DoorTrigger lastDoorTrigger = null;

        for (int i = 0; i < roomCount; i++)
        {
            GameObject roomPrefab = roomPrefabs[Random.Range(0, roomPrefabs.Length)];
            GameObject room = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity);
            spawnedRooms.Add(room);

            Transform entryPoint = room.transform.Find("DoorPoint_A");
            Transform exitPoint = room.transform.Find("DoorPoint_B");

            if (lastExitPoint != null && entryPoint != null)
            {
                Quaternion rotationDiff = lastExitPoint.rotation * Quaternion.Inverse(entryPoint.rotation);
                rotationDiff *= Quaternion.Euler(0, 180f, 0);
                room.transform.rotation = rotationDiff;
                Vector3 offset = lastExitPoint.position - entryPoint.position;
                room.transform.position += offset;
            }

            if (lastDoorTrigger != null)
                lastDoorTrigger.nextRoom = room;

            DoorTrigger exitTrigger = room.transform.Find("DoorTrigger_B")?.GetComponent<DoorTrigger>();
            lastDoorTrigger = exitTrigger;
            lastExitPoint = exitPoint;
<<<<<<< Updated upstream
        }
    }

    void BakeAndSpawnEnemies()
=======
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
>>>>>>> Stashed changes
    {
        if (EnemySpawner.Instance == null) return;

        foreach (GameObject room in spawnedRooms)
        {
            NavMeshSurface surface = room.GetComponent<NavMeshSurface>();
            if (surface != null)
                EnemySpawner.Instance.RegisterSurface(surface);
        }

        EnemySpawner.Instance.BakeAllAndSpawn();
    }
<<<<<<< Updated upstream
=======

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

            if (room != null)
                Destroy(room);
        }
    }
>>>>>>> Stashed changes
}