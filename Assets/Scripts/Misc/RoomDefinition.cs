using System;
using UnityEngine;

public enum RoomCategory
{
    SubmarineSpawn,
    Corridor,
    Common,
    Special,
    Water,
    Pool,
    Final
}

public enum RoomDoorDirection
{
    Any,
    North,
    South,
    East,
    West
}

[Serializable]
public class RoomDoorDefinition
{
    public string id = "Door";
    public Transform point;
    public DoorTrigger trigger;
    public RoomDoorDirection direction = RoomDoorDirection.Any;
    public bool canBeEntrance = true;
    public bool canBeExit = true;
    public bool startsOpen = true;
}

[DisallowMultipleComponent]
public class RoomDefinition : MonoBehaviour
{
    [Header("Identity")]
    public string roomName;
    public RoomCategory category = RoomCategory.Common;

    [Header("Spawn Rules")]
    [Min(0f)]
    public float spawnWeight = 1f;

    [Tooltip("Use -1 for no maximum.")]
    public int maxInstancesPerRun = -1;

    public bool canRepeat = true;

    [Header("Layout")]
    public Vector3 size = new Vector3(10f, 5f, 10f);
    public Vector3 boundsCenter = Vector3.zero;

    [Header("Doors")]
    public RoomDoorDefinition[] doors = new RoomDoorDefinition[0];

    public string DisplayName
    {
        get { return string.IsNullOrWhiteSpace(roomName) ? gameObject.name : roomName; }
    }

    public float EffectiveSpawnWeight
    {
        get { return Mathf.Max(0f, spawnWeight); }
    }

    public bool CanSpawn(int alreadyGenerated)
    {
        if (EffectiveSpawnWeight <= 0f) return false;
        if (!canRepeat && alreadyGenerated > 0) return false;
        return maxInstancesPerRun < 0 || alreadyGenerated < maxInstancesPerRun;
    }

    public bool TryGetEntrancePoint(out Transform point)
    {
        RoomDoorDefinition door = FindDoor(candidate => candidate.canBeEntrance && candidate.point != null);
        point = door != null ? door.point : null;
        return point != null;
    }

    public bool TryGetExitPoint(out Transform point)
    {
        RoomDoorDefinition door = FindDoor(candidate => candidate.canBeExit && candidate.point != null);
        point = door != null ? door.point : null;
        return point != null;
    }

    public bool TryGetExitTrigger(out DoorTrigger trigger)
    {
        RoomDoorDefinition door = FindDoor(candidate => candidate.canBeExit && candidate.trigger != null);
        trigger = door != null ? door.trigger : null;
        return trigger != null;
    }

    RoomDoorDefinition FindDoor(Predicate<RoomDoorDefinition> predicate)
    {
        if (doors == null || predicate == null) return null;

        for (int i = 0; i < doors.Length; i++)
        {
            RoomDoorDefinition door = doors[i];
            if (door != null && predicate(door))
                return door;
        }

        return null;
    }

    void Reset()
    {
        roomName = gameObject.name;

        Transform entryPoint = transform.Find("DoorPoint_A");
        Transform exitPoint = transform.Find("DoorPoint_B");
        DoorTrigger entryTrigger = transform.Find("DoorTrigger_A")?.GetComponent<DoorTrigger>();
        DoorTrigger exitTrigger = transform.Find("DoorTrigger_B")?.GetComponent<DoorTrigger>();

        if (entryPoint == null && exitPoint == null && entryTrigger == null && exitTrigger == null)
            return;

        doors = new[]
        {
            new RoomDoorDefinition
            {
                id = "A",
                point = entryPoint,
                trigger = entryTrigger,
                canBeEntrance = true,
                canBeExit = false
            },
            new RoomDoorDefinition
            {
                id = "B",
                point = exitPoint,
                trigger = exitTrigger,
                canBeEntrance = false,
                canBeExit = true
            }
        };
    }

    void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(roomName))
            roomName = gameObject.name;

        spawnWeight = Mathf.Max(0f, spawnWeight);
        size = new Vector3(
            Mathf.Max(0f, size.x),
            Mathf.Max(0f, size.y),
            Mathf.Max(0f, size.z));
    }

    void OnDrawGizmosSelected()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0f, 0.7f, 1f, 0.9f);
        Gizmos.DrawWireCube(boundsCenter, size);
        Gizmos.matrix = previousMatrix;

        if (doors == null) return;

        for (int i = 0; i < doors.Length; i++)
        {
            RoomDoorDefinition door = doors[i];
            if (door == null || door.point == null) continue;

            Gizmos.color = door.canBeExit ? Color.green : Color.cyan;
            Gizmos.DrawRay(door.point.position, door.point.forward * 1.25f);
            Gizmos.DrawWireSphere(door.point.position, 0.25f);
        }
    }
}
