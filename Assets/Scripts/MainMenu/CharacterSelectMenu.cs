using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CharacterSelectMenu : MonoBehaviour
{
    [Header("Assigned UI")]
    public GameObject panelRoot;
    public TMP_Text titleText;
    public TMP_Text selectedAgentText;
    public Button jennyPieButton;
    public Button sylvianButton;
    public Button secretAgentButton;
    public Button louiseButton;
    public Button confirmButton;
    public Button backButton;

    [Header("Generated UI")]
    public bool createGeneratedPanelIfMissing = true;
    public Color panelColor = new Color(0.02f, 0.025f, 0.03f, 0.96f);
    public Color buttonColor = new Color(0.08f, 0.14f, 0.17f, 1f);
    public Color selectedButtonColor = new Color(0.18f, 0.48f, 0.58f, 1f);
    public Color textColor = new Color(0.86f, 0.96f, 0.98f, 1f);

    private Action confirmCallback;
    private PlayerAgentType selectedAgent;
    private Canvas generatedCanvas;
    private bool cursorUnlockRequested;

    void Awake()
    {
        selectedAgent = AgentSelectionState.SelectedAgent;
        EnsureUi();
        RegisterListeners();
        Hide();
    }

    public void Show(Action onConfirm)
    {
        confirmCallback = onConfirm;
        selectedAgent = AgentSelectionState.SelectedAgent;
        EnsureUi();
        RegisterListeners();
        Refresh();

        if (generatedCanvas != null)
            generatedCanvas.gameObject.SetActive(true);

        if (panelRoot != null)
            panelRoot.SetActive(true);

        RequestCursorUnlock();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);

        ReleaseCursorUnlock();
    }

    void OnDestroy()
    {
        ReleaseCursorUnlock();
    }

    public void SelectJennyPie()
    {
        SelectAgent(PlayerAgentType.JennyPie);
    }

    public void SelectSylvian()
    {
        SelectAgent(PlayerAgentType.Sylvian);
    }

    public void SelectSecretAgent()
    {
        SelectAgent(PlayerAgentType.SecretAgent);
    }

    public void SelectLouise()
    {
        SelectAgent(PlayerAgentType.Louise);
    }

    public void Confirm()
    {
        AgentSelectionState.Select(selectedAgent);
        Hide();
        confirmCallback?.Invoke();
        confirmCallback = null;
    }

    public void Cancel()
    {
        Hide();
        confirmCallback = null;
    }

    void SelectAgent(PlayerAgentType agent)
    {
        selectedAgent = agent;
        Refresh();
    }

    void RequestCursorUnlock()
    {
        if (cursorUnlockRequested)
            return;

        CursorLockController.RequestCursorUnlocked();
        cursorUnlockRequested = true;
    }

    void ReleaseCursorUnlock()
    {
        if (!cursorUnlockRequested)
            return;

        CursorLockController.ReleaseCursorUnlocked();
        cursorUnlockRequested = false;
    }

    void EnsureUi()
    {
        if (panelRoot != null || !createGeneratedPanelIfMissing)
            return;

        generatedCanvas = GetComponentInChildren<Canvas>(true);
        if (generatedCanvas == null)
        {
            GameObject canvasObject = new GameObject(
                "CharacterSelectCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            generatedCanvas = canvasObject.GetComponent<Canvas>();
            generatedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            generatedCanvas.sortingOrder = 200;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        generatedCanvas.gameObject.SetActive(true);
        panelRoot = CreatePanel(generatedCanvas.transform);
        titleText = CreateText(panelRoot.transform, "Choose Agent", 34, TextAnchor.MiddleLeft);
        selectedAgentText = CreateText(panelRoot.transform, string.Empty, 22, TextAnchor.MiddleLeft);
        jennyPieButton = CreateButton(panelRoot.transform, "Jenny Pie");
        sylvianButton = CreateButton(panelRoot.transform, "Sylvian");
        secretAgentButton = CreateButton(panelRoot.transform, "Secret Agent");
        louiseButton = CreateButton(panelRoot.transform, "Louise");
        confirmButton = CreateButton(panelRoot.transform, "Confirm");
        backButton = CreateButton(panelRoot.transform, "Back");

        RectTransform panel = panelRoot.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(520f, 0f);

        LayoutElement titleLayout = titleText.gameObject.AddComponent<LayoutElement>();
        titleLayout.preferredHeight = 54f;

        LayoutElement selectedLayout = selectedAgentText.gameObject.AddComponent<LayoutElement>();
        selectedLayout.preferredHeight = 40f;
    }

    GameObject CreatePanel(Transform parent)
    {
        GameObject panel = new GameObject(
            "CharacterSelectPanel",
            typeof(RectTransform),
            typeof(Image),
            typeof(VerticalLayoutGroup));
        panel.transform.SetParent(parent, false);

        Image image = panel.GetComponent<Image>();
        image.color = panelColor;

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(32, 32, 32, 32);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return panel;
    }

    TMP_Text CreateText(Transform parent, string text, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        TMP_Text label = textObject.GetComponent<TMP_Text>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = textColor;
        label.alignment = ConvertAlignment(alignment);
        label.enableAutoSizing = true;
        label.fontSizeMin = 12;
        label.fontSizeMax = fontSize;
        return label;
    }

    Button CreateButton(Transform parent, string label)
    {
        GameObject buttonObject = new GameObject(
            label,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = buttonColor;

        LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
        layout.preferredHeight = 58f;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;

        TMP_Text text = CreateText(buttonObject.transform, label, 22, TextAnchor.MiddleCenter);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 4f);
        textRect.offsetMax = new Vector2(-12f, -4f);

        return button;
    }

    void RegisterListeners()
    {
        RegisterButton(jennyPieButton, SelectJennyPie);
        RegisterButton(sylvianButton, SelectSylvian);
        RegisterButton(secretAgentButton, SelectSecretAgent);
        RegisterButton(louiseButton, SelectLouise);
        RegisterButton(confirmButton, Confirm);
        RegisterButton(backButton, Cancel);
    }

    void RegisterButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    void Refresh()
    {
        if (selectedAgentText != null)
        {
            selectedAgentText.text =
                $"Selected: {AgentSelectionState.GetDisplayName(selectedAgent)}";
        }

        SetButtonSelected(jennyPieButton, selectedAgent == PlayerAgentType.JennyPie);
        SetButtonSelected(sylvianButton, selectedAgent == PlayerAgentType.Sylvian);
        SetButtonSelected(secretAgentButton, selectedAgent == PlayerAgentType.SecretAgent);
        SetButtonSelected(louiseButton, selectedAgent == PlayerAgentType.Louise);
    }

    void SetButtonSelected(Button button, bool selected)
    {
        if (button == null)
            return;

        Image image = button.GetComponent<Image>();
        if (image != null)
            image.color = selected ? selectedButtonColor : buttonColor;
    }

    TextAlignmentOptions ConvertAlignment(TextAnchor alignment)
    {
        switch (alignment)
        {
            case TextAnchor.MiddleCenter:
                return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight:
                return TextAlignmentOptions.Right;
            default:
                return TextAlignmentOptions.Left;
        }
    }
}
