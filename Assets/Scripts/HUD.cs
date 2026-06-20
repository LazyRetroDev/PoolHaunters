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
    public Image[] slotHighlights;

    [Header("Water")]
    public Slider waterBar;
    public Image waterFillImage;
    public PlayerStatus playerStatus;
    public Color cleanWaterColor = new Color(0.35f, 0.75f, 1f, 1f);
    public Color contaminatedWaterColor = new Color(0.35f, 0.9f, 0.25f, 1f);
    public Color chemicallyEnhancedWaterColor = new Color(1f, 0.85f, 0.25f, 1f);

    [Header("Death")]
    public bool hideWhenPlayerDies = true;

    private Canvas canvas;

    void Awake()
    {
        canvas = GetComponent<Canvas>();
    }

    void OnEnable()
    {
        if (playerStatus != null)
            playerStatus.OnDeath += HandlePlayerDeath;
    }

    void OnDisable()
    {
        if (playerStatus != null)
            playerStatus.OnDeath -= HandlePlayerDeath;
    }

    public void Bind(PlayerMovement newPlayer, PlayerInventory newInventory, PlayerStatus newPlayerStatus)
    {
        if (playerStatus != null)
            playerStatus.OnDeath -= HandlePlayerDeath;

        player = newPlayer;
        inventory = newInventory;
        playerStatus = newPlayerStatus;

        if (canvas == null)
            canvas = GetComponent<Canvas>();

        if (canvas != null)
            canvas.enabled = true;

        if (isActiveAndEnabled && playerStatus != null)
            playerStatus.OnDeath += HandlePlayerDeath;
    }

    void Update()
    {
        if (playerStatus != null && healthBar != null)
            healthBar.value = playerStatus.GetHealthPercent();

        if (playerStatus != null && healthText != null)
            healthText.text = Mathf.CeilToInt(playerStatus.GetCurrentHealth()).ToString();

        if (player != null && staminaBar != null)
            staminaBar.value = player.GetStaminaPercent();

        UpdateWaterHUD();
        UpdateInventoryHUD();
    }

    void HandlePlayerDeath(PlayerStatus deadPlayer)
    {
        if (!hideWhenPlayerDies || deadPlayer != playerStatus) return;

        if (canvas != null)
            canvas.enabled = false;
        else
            gameObject.SetActive(false);
    }

    void UpdateWaterHUD()
    {
        if (playerStatus == null) return;

        if (waterBar != null)
            waterBar.value = playerStatus.GetWaterPercent();

        WaterQuality quality = playerStatus.GetWaterQuality();

        if (waterFillImage != null)
            waterFillImage.color = GetWaterQualityColor(quality);
    }


    Color GetWaterQualityColor(WaterQuality quality)
    {
        switch (quality)
        {
            case WaterQuality.Contaminated:
                return contaminatedWaterColor;
            case WaterQuality.ChemicallyEnhanced:
                return chemicallyEnhancedWaterColor;
            default:
                return cleanWaterColor;
        }
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
                itemSlots[i].color = new Color(1f, 1f, 1f, 0f);
            }
        }
    }
}
