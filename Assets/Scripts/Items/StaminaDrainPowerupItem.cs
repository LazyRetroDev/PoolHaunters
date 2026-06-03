using UnityEngine;

[RequireComponent(typeof(Item))]
public class StaminaDrainPowerupItem : UsableItem
{
    public float staminaDrainMultiplier = 0.5f;
    public float duration = 10f;

    public override bool Use(PlayerInventory inventory, PlayerStatus playerStatus)
    {
        if (inventory == null) return false;

        PlayerMovement movement = inventory.GetComponent<PlayerMovement>();
        if (movement == null) return false;

        movement.ApplyStaminaDrainMultiplier(staminaDrainMultiplier, duration);
        return true;
    }
}
