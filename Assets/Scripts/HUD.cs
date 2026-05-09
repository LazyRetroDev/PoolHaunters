using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUD : MonoBehaviour
{
    public Slider healthBar;
    public TMP_Text healthText;
    public Slider staminaBar;
    public PlayerMovement player;
    public PlayerInventory inventory;

    public Image[] itemSlots;        
    public Image[] slotHighlights;   // optional highlight border
    public Slider waterBar;
    public PlayerStatus playerStatus;

    void Update()
    {
        if (playerStatus != null && healthBar != null)
            healthBar.value = playerStatus.GetHealthPercent();

        if (playerStatus != null && healthText != null)
            healthText.text = Mathf.CeilToInt(playerStatus.GetCurrentHealth()).ToString();

        if (player != null && staminaBar != null)
            staminaBar.value = player.GetStaminaPercent();

        if (playerStatus != null && waterBar != null)
            waterBar.value = playerStatus.GetWaterPercent();

        UpdateInventoryHUD();
    }

    void UpdateInventoryHUD()
    {
        if (inventory == null || itemSlots == null) return;

        Item[] slots = inventory.GetSlots();
        int selected = inventory.GetSelectedSlot();

        for (int i = 0; i < itemSlots.Length; i++)
        {
            if (i >= slots.Length) continue;

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
