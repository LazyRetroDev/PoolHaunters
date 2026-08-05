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

    [Header("Debug")]
    [SerializeField] private bool purchased;

    private float feedbackTimer;

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
        if (purchased && !canBuyMultipleTimes)
            return;

        if (!CanGrantPurchase(inventory))
        {
            ShowTemporaryText(cannotReceiveText);
            return;
        }

        if (!PlayerCurrencyState.SpendGerms(germCost))
        {
            ShowTemporaryText(insufficientFundsText);
            return;
        }

        if (!GrantPurchase(inventory))
        {
            PlayerCurrencyState.AddGerms(germCost);
            ShowTemporaryText(cannotReceiveText);
            return;
        }

        purchased = true;
        onPurchased?.Invoke();
        RefreshLabel();
    }

    bool CanGrantPurchase(PlayerInventory inventory)
    {
        switch (grantMode)
        {
            case PurchaseGrantMode.GiveToInventoryOrUseImmediately:
                return inventory != null && inventory.CanReceiveShopItem(itemPrefab);
            case PurchaseGrantMode.SpawnInWorld:
                return itemPrefab != null;
            default:
                return true;
        }
    }

    bool GrantPurchase(PlayerInventory inventory)
    {
        switch (grantMode)
        {
            case PurchaseGrantMode.GiveToInventoryOrUseImmediately:
                if (inventory == null)
                    return false;

                GetItemSpawnPose(inventory.transform, out Vector3 givePosition, out Quaternion giveRotation);
                return inventory.TryReceiveShopItem(itemPrefab, givePosition, giveRotation);

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
}
