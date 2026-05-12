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
        GenerateRooms();
        BakeAndSpawnEnemies();
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
        }
    }

    void BakeAndSpawnEnemies()
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
}