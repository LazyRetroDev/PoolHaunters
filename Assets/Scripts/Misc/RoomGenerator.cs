using UnityEngine;
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
    }

    void GenerateRooms()
    {
        Random.InitState(seed);

        Transform lastExitPoint = null;
        DoorTrigger lastDoorTrigger = null;

        for (int i = 0; i < roomCount; i++)
        {
            GameObject roomPrefab = roomPrefabs[Random.Range(0, roomPrefabs.Length)];

            // Spawn at origin first, then move
            GameObject room = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity);
            spawnedRooms.Add(room);

            Transform entryPoint = room.transform.Find("DoorPoint_A");
            Transform exitPoint = room.transform.Find("DoorPoint_B");

            if (lastExitPoint != null && entryPoint != null)
            {
                // First rotate the room so entry faces the last exit
                Quaternion rotationDiff = lastExitPoint.rotation * Quaternion.Inverse(entryPoint.rotation);
                rotationDiff *= Quaternion.Euler(0, 180f, 0); // flip to face inward
                room.transform.rotation = rotationDiff;

                // Then move the room so entry aligns with last exit
                Vector3 offset = lastExitPoint.position - entryPoint.position;
                room.transform.position += offset;
            }

            // Wire up door trigger
            if (lastDoorTrigger != null)
                lastDoorTrigger.nextRoom = room;

            DoorTrigger exitTrigger = room.transform.Find("DoorTrigger_B")?.GetComponent<DoorTrigger>();
            lastDoorTrigger = exitTrigger;
            lastExitPoint = exitPoint;
        }
    }
}