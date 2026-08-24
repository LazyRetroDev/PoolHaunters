using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class ShopPurchasePanel : MonoBehaviour
{
    private static ShopPurchasePanel instance;

    private Canvas canvas;
    private TMP_Text titleText;
    private TMP_Text descriptionText;
    private TMP_Text costText;
    private TMP_Text feedbackText;
    private TMP_Text confirmButtonText;
    private TMP_Text cancelButtonText;
    private Button confirmButton;
    private Button cancelButton;
    private ShopPurchaseStation activeStation;
    private PlayerInventory activeInventory;
    private PlayerStatus lockedPlayer;
    private bool controlsLocked;
    private float feedbackTimer;

    public static void Show(
        ShopPurchaseStation station,
        PlayerInventory inventory)
    {
        if (station == null)
            return;

        EnsureInstance();
        instance.Open(station, inventory);
    }

    static void EnsureInstance()
    {
        if (instance != null)
            return;

        GameObject panelObject = new GameObject("Shop Purchase Panel");
        instance = panelObject.AddComponent<ShopPurchasePanel>();
        DontDestroyOnLoad(panelObject);
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        EnsureCanvas();
        canvas.gameObject.SetActive(false);
    }

    void Update()
    {
        if (canvas == null || !canvas.gameObject.activeSelf)
            return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Close();

        if (feedbackTimer <= 0f)
            return;

        feedbackTimer -= Time.unscaledDeltaTime;
        if (feedbackTimer <= 0f && feedbackText != null)
            feedbackText.text = string.Empty;
    }

    void OnDisable()
    {
        ReleaseLocks();
    }

    void Open(
        ShopPurchaseStation station,
        PlayerInventory inventory)
    {
        ReleaseLocks();

        activeStation = station;
        activeInventory = inventory;
        activeStation.SetHighlighted(true);

        lockedPlayer = inventory != null
            ? inventory.GetComponent<PlayerStatus>()
            : null;
        if (lockedPlayer != null)
        {
            lockedPlayer.AddExternalControlLock();
            controlsLocked = true;
        }

        CursorLockController.RequestCursorUnlocked();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        EnsureCanvas();
        canvas.gameObject.SetActive(true);
        RefreshText();
    }

    void RefreshText()
    {
        if (activeStation == null)
            return;

        if (titleText != null)
            titleText.text = activeStation.ItemName;
        if (descriptionText != null)
            descriptionText.text = activeStation.ItemDescription;
        if (costText != null)
            costText.text = $"Cost: {activeStation.GermCost} germs\nYou have: {PlayerCurrencyState.Germs}";

        bool canPurchase = activeStation.CanPurchase(
            activeInventory,
            out string blockedReason,
            ignoreInventoryLock: true);
        if (feedbackText != null)
            feedbackText.text = blockedReason;

        if (confirmButton != null)
            confirmButton.interactable = canPurchase;
        if (confirmButtonText != null)
            confirmButtonText.text = activeStation.ConfirmButtonText;
        if (cancelButtonText != null)
            cancelButtonText.text = activeStation.CancelButtonText;
    }

    void ConfirmPurchase()
    {
        if (activeStation == null)
        {
            Close();
            return;
        }

        bool purchased = activeStation.TryPurchase(
            activeInventory,
            ignoreInventoryLock: true);
        if (purchased)
        {
            Close();
            return;
        }

        activeStation.CanPurchase(
            activeInventory,
            out string blockedReason,
            ignoreInventoryLock: true);
        if (feedbackText != null)
            feedbackText.text = string.IsNullOrWhiteSpace(blockedReason)
                ? activeStation.CannotReceiveText
                : blockedReason;

        feedbackTimer = 1.25f;
        RefreshText();
    }

    public void Close()
    {
        if (canvas != null)
            canvas.gameObject.SetActive(false);

        ReleaseLocks();
    }

    void ReleaseLocks()
    {
        if (activeStation != null)
            activeStation.SetHighlighted(false);

        activeStation = null;
        activeInventory = null;

        if (controlsLocked && lockedPlayer != null)
            lockedPlayer.RemoveExternalControlLock();

        lockedPlayer = null;
        controlsLocked = false;
        CursorLockController.ReleaseCursorUnlocked();
    }

    void EnsureCanvas()
    {
        if (canvas != null)
            return;

        EnsureEventSystem();

        GameObject canvasObject = new GameObject("Shop Purchase Canvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 230;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject shade = CreateRect(
            "Shade",
            canvasObject.transform,
            new Color(0f, 0f, 0f, 0.55f));
        RectTransform shadeRect = shade.GetComponent<RectTransform>();
        shadeRect.anchorMin = Vector2.zero;
        shadeRect.anchorMax = Vector2.one;
        shadeRect.offsetMin = Vector2.zero;
        shadeRect.offsetMax = Vector2.zero;

        GameObject panel = CreateRect(
            "Purchase Panel",
            shade.transform,
            new Color(0.03f, 0.04f, 0.04f, 0.96f));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(620f, 430f);
        panelRect.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(34, 34, 28, 28);
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        titleText = CreateText(panel.transform, "Item", 38f, FontStyles.Bold);
        descriptionText = CreateText(panel.transform, "Description", 24f, FontStyles.Normal);
        costText = CreateText(panel.transform, "Cost", 24f, FontStyles.Normal);
        feedbackText = CreateText(panel.transform, string.Empty, 22f, FontStyles.Bold);
        feedbackText.color = new Color(1f, 0.72f, 0.42f, 1f);

        GameObject buttonRow = new GameObject("Buttons", typeof(RectTransform));
        buttonRow.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup buttonLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
        buttonLayout.spacing = 16f;
        buttonLayout.childForceExpandWidth = true;
        buttonLayout.childForceExpandHeight = true;
        LayoutElement buttonRowLayout = buttonRow.AddComponent<LayoutElement>();
        buttonRowLayout.preferredHeight = 64f;

        confirmButton = CreateButton(buttonRow.transform, "BUY", ConfirmPurchase);
        confirmButtonText = confirmButton.GetComponentInChildren<TMP_Text>();
        cancelButton = CreateButton(buttonRow.transform, "CANCEL", Close);
        cancelButtonText = cancelButton.GetComponentInChildren<TMP_Text>();
    }

    GameObject CreateRect(
        string objectName,
        Transform parent,
        Color color)
    {
        GameObject obj = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        return obj;
    }

    TMP_Text CreateText(
        Transform parent,
        string text,
        float fontSize,
        FontStyles style)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        TMP_Text label = textObject.GetComponent<TMP_Text>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = new Color(0.85f, 1f, 0.96f, 1f);
        label.textWrappingMode = TextWrappingModes.Normal;

        LayoutElement layout = textObject.AddComponent<LayoutElement>();
        layout.minHeight = fontSize + 8f;
        layout.preferredHeight = style == FontStyles.Bold ? fontSize + 18f : fontSize * 2.4f;
        return label;
    }

    Button CreateButton(
        Transform parent,
        string text,
        UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(
            text,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.08f, 0.16f, 0.18f, 1f);

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);

        TMP_Text label = CreateText(buttonObject.transform, text, 24f, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.Center;
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return button;
    }

    void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }
}
