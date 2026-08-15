using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerStatus))]
public class PlayerReviveInteractable : MonoBehaviour, IPlayerInteractable
{
    private PlayerStatus playerStatus;

    void Awake()
    {
        playerStatus = GetComponent<PlayerStatus>();
    }

    public void Interact(PlayerInventory inventory)
    {
        if (playerStatus == null)
            playerStatus = GetComponent<PlayerStatus>();

        if (playerStatus == null ||
            !playerStatus.IsKnockedOut() ||
            playerStatus.IsDead() ||
            playerStatus.IsTransformed())
        {
            return;
        }

        playerStatus.RequestReviveFrom(inventory);
    }
}
