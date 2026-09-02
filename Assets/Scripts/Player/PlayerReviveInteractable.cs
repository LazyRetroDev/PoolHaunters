using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerStatus))]
public class PlayerReviveInteractable : MonoBehaviour, IPlayerInteractable
{
    public string revivePrompt = "E - Reviver";

    private PlayerStatus playerStatus;

    void Awake()
    {
        playerStatus = GetComponent<PlayerStatus>();
    }

    public void Interact(PlayerInventory inventory)
    {
        if (!CanInteract(inventory))
        {
            return;
        }

        playerStatus.RequestReviveFrom(inventory);
    }

    public bool TryGetInteractionPrompt(
        PlayerInventory inventory,
        out string prompt)
    {
        prompt = string.Empty;

        if (!CanInteract(inventory))
            return false;

        prompt = string.IsNullOrWhiteSpace(revivePrompt)
            ? "E - Reviver"
            : revivePrompt;
        return true;
    }

    bool CanInteract(PlayerInventory inventory)
    {
        if (playerStatus == null)
            playerStatus = GetComponent<PlayerStatus>();

        if (playerStatus == null || inventory == null)
            return false;

        PlayerStatus reviver = inventory.GetComponent<PlayerStatus>();
        return playerStatus.CanBeRevivedBy(reviver);
    }
}
