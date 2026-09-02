using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class ShopPurchaseStation : MonoBehaviour, IPlayerInteractable
{
    public enum PurchaseGrantMode
    {
        None,
        GiveToInventoryOrUseImmediately,
        SpawnInWorld,
        ApplySessionUpgrade
    }

    public enum SessionUpgradeType
    {
        MaxHealth,
        MaxStamina,
        MaxWater
    }

    [Header("Purchase")]
    [SerializeField] private string itemName = "Upgrade";
    [SerializeField, TextArea(2, 5)] private string itemDescription = "Purchase this upgrade.";
    [SerializeField, Min(0)] private int germCost = 25;
    [SerializeField] private bool canBuyMultipleTimes = false;
    [SerializeField] private UnityEvent onPurchased;

    [Header("Item Grant")]
    [SerializeField] private PurchaseGrantMode grantMode = PurchaseGrantMode.None;
    [SerializeField] private GameObject itemPrefab;
    [SerializeField] private Transform itemSpawnPoint;
    [SerializeField] private float spawnInFrontDistance = 1f;
    [SerializeField] private string cannotReceiveText = "No room";

    [Header("Session Upgrade")]
    [SerializeField] private SessionUpgradeType sessionUpgradeType =
        SessionUpgradeType.MaxHealth;
    [SerializeField, Min(0f)] private float sessionUpgradeAmount = 25f;
    [SerializeField] private string upgradeUnavailableText = "Upgrade unavailable";

    [Header("Label")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private string availableFormat = "{0}\n{1} germs";
    [SerializeField] private string boughtText = "SOLD";
    [SerializeField] private string insufficientFundsText = "Not enough germs";
    [SerializeField] private string waterFullText = "Water full";
    [SerializeField] private float feedbackSeconds = 1.25f;

    [Header("Info Panel")]
    [SerializeField] private bool useConfirmationPanel = true;
    [SerializeField] private string confirmButtonText = "BUY";
    [SerializeField] private string cancelButtonText = "CANCEL";
    [SerializeField] private GameObject highlightRoot;
    [SerializeField] private bool highlightStationWhilePanelOpen = true;

    [Header("Debug")]
    [SerializeField] private bool purchased;

    private static readonly HashSet<SessionUpgradeType> boughtUpgradesThisVisit =
        new HashSet<SessionUpgradeType>();
    private static int activeShopVisitSceneHandle = -1;

    private float feedbackTimer;

    public string ItemName => itemName;
    public string ItemDescription => itemDescription;
    public int GermCost => germCost;
    public string CannotReceiveText => cannotReceiveText;
    public string ConfirmButtonText => confirmButtonText;
    public string CancelButtonText => cancelButtonText;
    public bool IsPurchased => purchased;
    public bool CanBuyMultipleTimes => canBuyMultipleTimes;

    void Awake()
    {
        RefreshShopVisitState();

        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);

        RefreshLabel();
    }

    void Update()
    {
        if (feedbackTimer <= 0f)
            return;

        feedbackTimer -= Time.deltaTime;
        if (feedbackTimer <= 0f)
            RefreshLabel();
    }

    void OnValidate()
    {
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);

        germCost = Mathf.Max(0, germCost);
        sessionUpgradeAmount = Mathf.Max(0f, sessionUpgradeAmount);
        RefreshLabel();
    }

    public void Interact(PlayerInventory inventory)
    {
        if (useConfirmationPanel)
        {
            ShopPurchasePanel.Show(this, inventory);
            return;
        }

        TryPurchase(inventory);
    }

    public bool TryPurchase(
        PlayerInventory inventory,
        bool ignoreInventoryLock = false)
    {
        RefreshShopVisitState();

        if (IsStationPurchaseLocked())
            return false;

        if (IsSessionUpgradeBoughtThisVisit())
        {
            ShowTemporaryText(boughtText);
            return false;
        }

        if (!CanGrantPurchase(inventory, ignoreInventoryLock))
        {
            ShowTemporaryText(GetCannotReceiveReason(inventory));
            return false;
        }

        if (!PlayerCurrencyState.SpendGerms(germCost))
        {
            ShowTemporaryText(insufficientFundsText);
            return false;
        }

        if (!GrantPurchase(inventory, ignoreInventoryLock))
        {
            PlayerCurrencyState.AddGerms(germCost);
            ShowTemporaryText(GetCannotReceiveReason(inventory));
            return false;
        }

        if (grantMode != PurchaseGrantMode.ApplySessionUpgrade)
            purchased = true;

        MarkSessionUpgradeBoughtThisVisit();
        onPurchased?.Invoke();
        RefreshLabel();
        return true;
    }

    public bool CanPurchase(
        PlayerInventory inventory,
        out string blockedReason,
        bool ignoreInventoryLock = false)
    {
        RefreshShopVisitState();

        if (IsStationPurchaseLocked())
        {
            blockedReason = boughtText;
            return false;
        }

        if (IsSessionUpgradeBoughtThisVisit())
        {
            blockedReason = boughtText;
            return false;
        }

        if (!CanGrantPurchase(inventory, ignoreInventoryLock))
        {
            blockedReason = GetCannotReceiveReason(inventory);
            return false;
        }

        if (PlayerCurrencyState.Germs < germCost)
        {
            blockedReason = insufficientFundsText;
            return false;
        }

        blockedReason = string.Empty;
        return true;
    }

    public void SetHighlighted(bool active)
    {
        if (highlightRoot != null)
            highlightRoot.SetActive(active && highlightStationWhilePanelOpen);
    }

    bool CanGrantPurchase(
        PlayerInventory inventory,
        bool ignoreInventoryLock = false)
    {
        switch (grantMode)
        {
            case PurchaseGrantMode.GiveToInventoryOrUseImmediately:
                return inventory != null &&
                    inventory.CanReceiveShopItem(itemPrefab, ignoreInventoryLock);
            case PurchaseGrantMode.SpawnInWorld:
                return itemPrefab != null;
            case PurchaseGrantMode.ApplySessionUpgrade:
                return CanApplySessionUpgrade(inventory);
            default:
                return true;
        }
    }

    string GetCannotReceiveReason(PlayerInventory inventory)
    {
        if (grantMode == PurchaseGrantMode.ApplySessionUpgrade)
            return upgradeUnavailableText;

        if (inventory == null || itemPrefab == null)
            return cannotReceiveText;

        WaterItem waterItem = itemPrefab.GetComponentInChildren<WaterItem>(true);
        PlayerStatus playerStatus = inventory.GetComponent<PlayerStatus>();
        if (waterItem != null &&
            waterItem.useImmediatelyOnPickup &&
            playerStatus != null &&
            waterItem.waterAmount > 0f &&
            playerStatus.GetWaterSpace() <= 0f)
        {
            return waterFullText;
        }

        return cannotReceiveText;
    }

    bool CanApplySessionUpgrade(PlayerInventory inventory)
    {
        if (inventory == null || sessionUpgradeAmount <= 0f)
            return false;

        switch (sessionUpgradeType)
        {
            case SessionUpgradeType.MaxHealth:
            case SessionUpgradeType.MaxWater:
                return inventory.GetComponent<PlayerStatus>() != null;
            case SessionUpgradeType.MaxStamina:
                return inventory.GetComponent<PlayerMovement>() != null;
            default:
                return false;
        }
    }

    bool GrantPurchase(
        PlayerInventory inventory,
        bool ignoreInventoryLock = false)
    {
        switch (grantMode)
        {
            case PurchaseGrantMode.GiveToInventoryOrUseImmediately:
                if (inventory == null)
                    return false;

                GetItemSpawnPose(inventory.transform, out Vector3 givePosition, out Quaternion giveRotation);
                return inventory.TryReceiveShopItem(
                    itemPrefab,
                    givePosition,
                    giveRotation,
                    ignoreInventoryLock);

            case PurchaseGrantMode.SpawnInWorld:
                if (inventory == null)
                    return false;

                GetItemSpawnPose(inventory.transform, out Vector3 spawnPosition, out Quaternion spawnRotation);
                return inventory.TrySpawnShopItemInWorld(itemPrefab, spawnPosition, spawnRotation);

            case PurchaseGrantMode.ApplySessionUpgrade:
                return ApplySessionUpgrade(inventory);

            default:
                return true;
        }
    }

    bool ApplySessionUpgrade(PlayerInventory inventory)
    {
        if (inventory == null || sessionUpgradeAmount <= 0f)
            return false;

        switch (sessionUpgradeType)
        {
            case SessionUpgradeType.MaxHealth:
                PlayerStatus healthStatus = inventory.GetComponent<PlayerStatus>();
                return healthStatus != null &&
                    healthStatus.AddSessionMaxHealth(sessionUpgradeAmount);

            case SessionUpgradeType.MaxWater:
                PlayerStatus waterStatus = inventory.GetComponent<PlayerStatus>();
                return waterStatus != null &&
                    waterStatus.AddSessionMaxWater(sessionUpgradeAmount);

            case SessionUpgradeType.MaxStamina:
                PlayerMovement movement = inventory.GetComponent<PlayerMovement>();
                return movement != null &&
                    movement.AddSessionMaxStamina(sessionUpgradeAmount);

            default:
                return false;
        }
    }

    void GetItemSpawnPose(
        Transform buyer,
        out Vector3 position,
        out Quaternion rotation)
    {
        if (itemSpawnPoint != null)
        {
            position = itemSpawnPoint.position;
            rotation = itemSpawnPoint.rotation;
            return;
        }

        Transform origin = buyer != null ? buyer : transform;
        Vector3 forward = origin.forward.sqrMagnitude > 0.001f
            ? origin.forward.normalized
            : transform.forward;

        position = origin.position + forward * Mathf.Max(0.1f, spawnInFrontDistance);
        rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    void ShowTemporaryText(string text)
    {
        if (label != null)
            label.text = text;

        feedbackTimer = Mathf.Max(0.1f, feedbackSeconds);
    }

    void RefreshLabel()
    {
        if (label == null)
            return;

        RefreshShopVisitState();

        if (IsStationPurchaseLocked() ||
            IsSessionUpgradeBoughtThisVisit())
        {
            label.text = boughtText;
            return;
        }

        label.text = string.Format(availableFormat, itemName, germCost);
    }

    static void RefreshShopVisitState()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        int sceneHandle = activeScene.handle;
        if (sceneHandle == activeShopVisitSceneHandle)
            return;

        activeShopVisitSceneHandle = sceneHandle;
        boughtUpgradesThisVisit.Clear();
    }

    bool IsSessionUpgradeBoughtThisVisit()
    {
        return grantMode == PurchaseGrantMode.ApplySessionUpgrade &&
            boughtUpgradesThisVisit.Contains(sessionUpgradeType);
    }

    bool IsStationPurchaseLocked()
    {
        return grantMode != PurchaseGrantMode.ApplySessionUpgrade &&
            purchased &&
            !canBuyMultipleTimes;
    }

    void MarkSessionUpgradeBoughtThisVisit()
    {
        if (grantMode != PurchaseGrantMode.ApplySessionUpgrade)
            return;

        boughtUpgradesThisVisit.Add(sessionUpgradeType);
    }

    void OnDisable()
    {
        SetHighlighted(false);
    }
}
