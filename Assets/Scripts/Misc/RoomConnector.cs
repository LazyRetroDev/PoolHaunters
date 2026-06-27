using UnityEngine;

public enum RoomConnectorState
{
    Open,
    Connected,
    Closed
}

[DisallowMultipleComponent]
public class RoomConnector : MonoBehaviour
{
    [Header("Identity")]
    public string id = "Connector";

    [Header("Rules")]
    public RoomDoorDirection direction = RoomDoorDirection.Any;
    public bool enforceDirectionCompatibility;
    public bool canBeEntrance = true;
    public bool canBeExit = true;
    public bool startsOpen = true;

    [Header("Door")]
    public DoorTrigger trigger;

    [SerializeField] private RoomConnectorState state = RoomConnectorState.Open;
    [SerializeField] private RoomConnector connectedTo;

    public RoomConnectorState State
    {
        get { return state; }
    }

    public RoomConnector ConnectedTo
    {
        get { return connectedTo; }
    }

    public bool IsAvailable
    {
        get { return state == RoomConnectorState.Open && connectedTo == null; }
    }

    public Transform Point
    {
        get { return transform; }
    }

    public DoorTrigger Trigger
    {
        get
        {
            if (trigger == null)
                trigger = GetComponent<DoorTrigger>();

            return trigger;
        }
    }

    void Awake()
    {
        ResetRuntimeState();
    }

    public void ResetRuntimeState()
    {
        connectedTo = null;
        state = startsOpen ? RoomConnectorState.Open : RoomConnectorState.Closed;
    }

    public bool CanConnectTo(RoomConnector other)
    {
        if (other == null || other == this) return false;
        if (!canBeExit || !other.canBeEntrance) return false;
        if (!IsAvailable || !other.IsAvailable) return false;
        if (!enforceDirectionCompatibility && !other.enforceDirectionCompatibility)
            return true;

        return DirectionsAreCompatible(direction, other.direction);
    }

    public bool TryConnect(RoomConnector other)
    {
        if (!CanConnectTo(other))
            return false;

        connectedTo = other;
        state = RoomConnectorState.Connected;

        other.connectedTo = this;
        other.state = RoomConnectorState.Connected;
        return true;
    }

    public void Open()
    {
        connectedTo = null;
        state = RoomConnectorState.Open;
    }

    public void Close()
    {
        connectedTo = null;
        state = RoomConnectorState.Closed;
    }

    bool DirectionsAreCompatible(RoomDoorDirection first, RoomDoorDirection second)
    {
        if (first == RoomDoorDirection.Any || second == RoomDoorDirection.Any)
            return true;

        return
            first == RoomDoorDirection.North && second == RoomDoorDirection.South ||
            first == RoomDoorDirection.South && second == RoomDoorDirection.North ||
            first == RoomDoorDirection.East && second == RoomDoorDirection.West ||
            first == RoomDoorDirection.West && second == RoomDoorDirection.East;
    }

    void Reset()
    {
        id = gameObject.name;
        trigger = GetComponent<DoorTrigger>();
        ResetRuntimeState();
    }

    void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(id))
            id = gameObject.name;

        if (trigger == null)
            trigger = GetComponent<DoorTrigger>();

        if (!Application.isPlaying)
            state = startsOpen ? RoomConnectorState.Open : RoomConnectorState.Closed;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = GetGizmoColor();
        Gizmos.DrawWireSphere(transform.position, 0.25f);
        Gizmos.DrawRay(transform.position, transform.forward * 1.25f);
    }

    Color GetGizmoColor()
    {
        if (state == RoomConnectorState.Closed)
            return Color.red;

        if (state == RoomConnectorState.Connected)
            return Color.green;

        return canBeExit ? Color.yellow : Color.cyan;
    }
}
