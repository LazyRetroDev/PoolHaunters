using UnityEngine;

public abstract class UsableItem : MonoBehaviour
{
    public bool consumeOnUse = true;

    public abstract bool Use(PlayerInventory inventory, PlayerStatus playerStatus);
}
