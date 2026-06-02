using UnityEngine;

public class DoorTrigger : MonoBehaviour
{
    private RoomGenerator generator;
    private bool hasTriggered;

    public void Initialize(RoomGenerator roomGenerator)
    {
        generator = roomGenerator;
        hasTriggered = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (hasTriggered || !other.CompareTag("Player")) return;
        if (generator == null) return;

        hasTriggered = true;
        generator.GenerateNextRoomFromDoor(this);
    }
}
