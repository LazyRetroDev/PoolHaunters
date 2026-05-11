using UnityEngine;

public class DoorTrigger : MonoBehaviour
{
    public GameObject nextRoom;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (nextRoom != null)
            nextRoom.SetActive(true);
    }
}