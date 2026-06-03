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
        if (!isPetrified) return;

        petrifyTimer -= Time.deltaTime;
        ApplyPetrifiedControlLock();

        if (petrifyTimer <= 0f)
            Unpetrify();
    }

    public bool IsPetrified()
    {
        return isPetrified;
    }

    public void Petrify()
    {
        isPetrified = true;
        petrifyTimer = petrifyDuration;
        ApplyPetrifiedControlLock();
    }

    public void Unpetrify()
    {
        isPetrified = false;
        if (movement != null) movement.enabled = true;
        if (inventory != null) inventory.enabled = true;
    }

    void ApplyPetrifiedControlLock()
    {
        if (movement != null) movement.enabled = false;
        if (inventory != null) inventory.enabled = false;
    }
}
