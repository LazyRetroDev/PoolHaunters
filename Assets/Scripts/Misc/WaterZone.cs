using UnityEngine;

public class WaterZone : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        PlayerStatus status = other.GetComponent<PlayerStatus>();
        if (status != null) status.SetInWater(true);
    }

    void OnTriggerExit(Collider other)
    {
        PlayerStatus status = other.GetComponent<PlayerStatus>();
        if (status != null) status.SetInWater(false);
    }
}