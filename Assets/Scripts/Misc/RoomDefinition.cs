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

    [Min(0)]
    [Tooltip("The earliest generated-room index where this prefab may appear. The starting room is index 0.")]
    public int minimumRoomIndex;

    [Min(0)]
    [Tooltip("How many other rooms must appear before this same prefab can repeat.")]
    public int minimumRoomsBetweenRepeats;

    [Header("Layout")]
    public Vector3 size = new Vector3(10f, 5f, 10f);
    public Vector3 boundsCenter = Vector3.zero;

    [Header("Connectors")]
    public RoomConnector[] connectors = new RoomConnector[0];

    [Header("Legacy Doors")]
    public RoomDoorDefinition[] doors = new RoomDoorDefinition[0];

    public string DisplayName
    {
        get { return string.IsNullOrWhiteSpace(roomName) ? gameObject.name : roomName; }
    }

    public float EffectiveSpawnWeight
    {
        get { return Mathf.Max(0f, spawnWeight); }
    }

    public bool HasConnectorDefinitions
    {
        get { return HasAnyConnector(); }
    }

    public bool CanSpawn(
        int alreadyGenerated,
        int roomIndex,
        int roomsSinceLastInstance)
    {
        if (EffectiveSpawnWeight <= 0f) return false;
        if (roomIndex < minimumRoomIndex) return false;
        if (!canRepeat && alreadyGenerated > 0) return false;
        if (alreadyGenerated > 0 &&
            roomsSinceLastInstance <= minimumRoomsBetweenRepeats)
        {
            return false;
        }

        return maxInstancesPerRun < 0 ||
            alreadyGenerated < maxInstancesPerRun;
    }

    public Bounds GetWorldBounds()
    {
        Vector3 halfSize = size * 0.5f;
        Vector3[] corners =
        {
            boundsCenter + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z),
            boundsCenter + new Vector3(-halfSize.x, -halfSize.y, halfSize.z),
            boundsCenter + new Vector3(-halfSize.x, halfSize.y, -halfSize.z),
            boundsCenter + new Vector3(-halfSize.x, halfSize.y, halfSize.z),
            boundsCenter + new Vector3(halfSize.x, -halfSize.y, -halfSize.z),
            boundsCenter + new Vector3(halfSize.x, -halfSize.y, halfSize.z),
            boundsCenter + new Vector3(halfSize.x, halfSize.y, -halfSize.z),
            boundsCenter + new Vector3(halfSize.x, halfSize.y, halfSize.z)
        };

        Bounds worldBounds = new Bounds(transform.TransformPoint(corners[0]), Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
            worldBounds.Encapsulate(transform.TransformPoint(corners[i]));

        return worldBounds;
    }

    public bool TryGetEntrancePoint(out Transform point)
    {
        RoomConnector connector;
        if (TryGetEntranceConnector(out connector))
        {
            point = connector.Point;
            return point != null;
        }

        if (HasConnectorDefinitions)
        {
            point = null;
            return false;
        }

        RoomDoorDefinition door = FindDoor(candidate => candidate.canBeEntrance && candidate.point != null);
        point = door != null ? door.point : null;
        return point != null;
    }

    public bool TryGetExitPoint(out Transform point)
    {
        RoomConnector connector;
        if (TryGetExitConnector(out connector))
        {
            point = connector.Point;
            return point != null;
        }

        if (HasConnectorDefinitions)
        {
            point = null;
            return false;
        }

        RoomDoorDefinition door = FindDoor(candidate => candidate.canBeExit && candidate.point != null);
        point = door != null ? door.point : null;
        return point != null;
    }

    public bool TryGetExitTrigger(out DoorTrigger trigger)
    {
        RoomConnector connector;
        if (TryGetExitConnector(out connector) && connector.Trigger != null)
        {
            trigger = connector.Trigger;
            return true;
        }

        if (HasConnectorDefinitions)
        {
            trigger = null;
            return false;
        }

        RoomDoorDefinition door = FindDoor(candidate => candidate.canBeExit && candidate.trigger != null);
        trigger = door != null ? door.trigger : null;
        return trigger != null;
    }

    public bool TryGetEntranceConnector(out RoomConnector connector)
    {
        connector = FindConnector(candidate => candidate.canBeEntrance && candidate.IsAvailable);
        return connector != null;
    }

    public bool TryGetEntranceConnector(RoomConnector source, out RoomConnector connector)
    {
        connector = FindConnector(candidate => source != null && source.CanConnectTo(candidate));
        return connector != null;
    }

    public bool TryGetExitConnector(out RoomConnector connector)
    {
        connector = FindConnector(candidate => candidate.canBeExit && candidate.IsAvailable);
        return connector != null;
    }

    public RoomConnector FindConnector(Predicate<RoomConnector> predicate)
    {
        if (connectors == null || predicate == null) return null;

        for (int i = 0; i < connectors.Length; i++)
        {
            RoomConnector connector = connectors[i];
            if (connector != null && predicate(connector))
                return connector;
        }

        return null;
    }

    bool HasAnyConnector()
    {
        if (connectors == null) return false;

        for (int i = 0; i < connectors.Length; i++)
        {
            if (connectors[i] != null)
                return true;
        }

        return false;
    }

    [ContextMenu("Refresh Connectors")]
    public void RefreshConnectors()
    {
        connectors = GetComponentsInChildren<RoomConnector>(true);
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
        RefreshConnectors();

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
        minimumRoomIndex = Mathf.Max(0, minimumRoomIndex);
        minimumRoomsBetweenRepeats = Mathf.Max(
            0,
            minimumRoomsBetweenRepeats);
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

        if (connectors != null && connectors.Length > 0)
        {
            for (int i = 0; i < connectors.Length; i++)
            {
                RoomConnector connector = connectors[i];
                if (connector == null) continue;

                Gizmos.color = connector.canBeExit ? Color.green : Color.cyan;
                Gizmos.DrawRay(connector.Point.position, connector.Point.forward * 1.25f);
                Gizmos.DrawWireSphere(connector.Point.position, 0.25f);
            }

            return;
        }

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
