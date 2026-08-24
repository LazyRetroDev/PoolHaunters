using TMPro;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class ShopPurchaseStation : MonoBehaviour, IPlayerInteractable
{
    public enum PurchaseGrantMode
    {
        None,
        GiveToInventoryOrUseImmediately,
        SpawnInWorld
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

    [Header("Label")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private string availableFormat = "{0}\n{1} germs";
    [SerializeField] private string boughtText = "SOLD";
    [SerializeField] private string insufficientFundsText = "Not enough germs";
    [SerializeField] private float feedbackSeconds = 1.25f;

    [Header("Info Panel")]
    [SerializeField] private bool useConfirmationPanel = true;
    [SerializeField] private string confirmButtonText = "BUY";
    [SerializeField] private string cancelButtonText = "CANCEL";
    [SerializeField] private GameObject highlightRoot;
    [SerializeField] private bool highlightStationWhilePanelOpen = true;

    [Header("Debug")]
    [SerializeField] private bool purchased;

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
        if (purchased && !canBuyMultipleTimes)
            return false;

        if (!CanGrantPurchase(inventory, ignoreInventoryLock))
        {
            ShowTemporaryText(cannotReceiveText);
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
            ShowTemporaryText(cannotReceiveText);
            return false;
        }

        purchased = true;
        onPurchased?.Invoke();
        RefreshLabel();
        return true;
    }

    public bool CanPurchase(
        PlayerInventory inventory,
        out string blockedReason,
        bool ignoreInventoryLock = false)
    {
        if (purchased && !canBuyMultipleTimes)
        {
            blockedReason = boughtText;
            return false;
        }

        if (!CanGrantPurchase(inventory, ignoreInventoryLock))
        {
            blockedReason = cannotReceiveText;
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
            default:
                return true;
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

            default:
                return true;
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

        if (purchased && !canBuyMultipleTimes)
        {
            label.text = boughtText;
            return;
        }

        label.text = string.Format(availableFormat, itemName, germCost);
    }

    void OnDisable()
    {
        SetHighlighted(false);
    }
}
