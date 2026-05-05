using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUD : MonoBehaviour
{
    public Slider staminaBar;
    public PlayerMovement player;
    public PlayerInventory inventory;

    public Image[] itemSlots;        
    public Image[] slotHighlights;   // optional highlight border
    public Slider waterBar;
    public PlayerStatus playerStatus;

    void Update()
    {
        staminaBar.value = player.GetStaminaPercent();
        waterBar.value = playerStatus.GetWaterPercent();
        UpdateInventoryHUD();
    }

    void UpdateInventoryHUD()
    {
        Item[] slots = inventory.GetSlots();
        int selected = inventory.GetSelectedSlot();

        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (slotHighlights != null && slotHighlights.Length > i)
                slotHighlights[i].enabled = i == selected;

            if (slots[i] != null && slots[i].itemIcon != null)
            {
                itemSlots[i].sprite = slots[i].itemIcon;
                itemSlots[i].color = Color.white;
            }
            else
            {
                itemSlots[i].sprite = null;
                itemSlots[i].color = new Color(1f, 1f, 1f, 0f); // transparent,
            }
        }
    }
}