using UnityEngine;

public class PlayerPetrify : MonoBehaviour
{
    public float petrifyDuration = 10f;
    private bool isPetrified = false;
    private float petrifyTimer;

    private PlayerMovement movement;
    private PlayerInventory inventory;

    void Start()
    {
        movement = GetComponent<PlayerMovement>();
        inventory = GetComponent<PlayerInventory>();
    }

    void Update()
    {
        if (isPetrified)
        {
            petrifyTimer -= Time.deltaTime;
            if (movement != null) movement.enabled = false;
            if (inventory != null) inventory.enabled = false;
            if (petrifyTimer <= 0f) Unpetrify();
        }
    }

    public bool IsPetrified()
    {
        return isPetrified;
    }

    public void Petrify()
    {
        isPetrified = true;
        petrifyTimer = petrifyDuration;
    }

    public void Unpetrify()
    {
        isPetrified = false;
        if (movement != null) movement.enabled = true;
        if (inventory != null) inventory.enabled = true;
    }
}
