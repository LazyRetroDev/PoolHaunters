using TMPro;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class ShopPurchaseStation : MonoBehaviour, IPlayerInteractable
{
    [Header("Purchase")]
    [SerializeField] private string itemName = "Upgrade";
    [SerializeField, Min(0)] private int germCost = 25;
    [SerializeField] private bool canBuyMultipleTimes = false;
    [SerializeField] private UnityEvent onPurchased;

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

        if (!PlayerCurrencyState.SpendGerms(germCost))
        {
            ShowTemporaryText(insufficientFundsText);
            return;
        }

        purchased = true;
        onPurchased?.Invoke();
        RefreshLabel();
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
