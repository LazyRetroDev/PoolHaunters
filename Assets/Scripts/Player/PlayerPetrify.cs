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
            movement.enabled = false;
            inventory.enabled = false;
            if (petrifyTimer <= 0f) Unpetrify();
        }
    }

    public void Petrify()
    {
        isPetrified = true;
        petrifyTimer = petrifyDuration;
    }

    public void Unpetrify()
    {
        isPetrified = false;
        movement.enabled = true;
        inventory.enabled = true;
    }
}