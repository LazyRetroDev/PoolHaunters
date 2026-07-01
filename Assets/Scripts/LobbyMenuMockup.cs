using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class LobbyMenuMockup : MonoBehaviour
{
    private static readonly Color Background = new Color(0.035f, 0.04f, 0.045f, 0.97f);
    private static readonly Color Panel = new Color(0.09f, 0.1f, 0.105f, 1f);
    private static readonly Color PanelAlt = new Color(0.14f, 0.145f, 0.14f, 1f);
    private static readonly Color Accent = new Color(0.3f, 0.72f, 0.55f, 1f);
    private static readonly Color Warning = new Color(0.92f, 0.7f, 0.25f, 1f);
    private static readonly Color Danger = new Color(0.78f, 0.27f, 0.24f, 1f);
    private static readonly Color TextPrimary = new Color(0.94f, 0.95f, 0.92f, 1f);
    private static readonly Color TextMuted = new Color(0.62f, 0.65f, 0.62f, 1f);

    private MainMenu mainMenu;
    private GameObject root;
    private GameObject homePanel;
    private GameObject createPanel;
    private GameObject joinPanel;
    private GameObject roomPanel;

    private TMP_InputField roomNameInput;
    private TMP_InputField hostNameInput;
    private TMP_InputField joinCodeInput;
    private TMP_InputField joinNameInput;
    private TMP_Text playerCountText;
    private TMP_Text regionText;
    private TMP_Text difficultyText;
    private TMP_Text privacyText;
    private TMP_Text roomTitleText;
    private TMP_Text roomCodeText;
    private TMP_Text roomSettingsText;
    private TMP_Text readyButtonText;
    private TMP_Text startButtonText;
    private Button startButton;
    private readonly TMP_Text[] playerRows = new TMP_Text[4];

    private int maximumPlayers = 4;
    private int regionIndex;
    private int difficultyIndex = 1;
    private bool isPrivate = true;
    private bool localPlayerReady;
    private bool localPlayerIsHost;
    private string currentRoomCode;
    private string currentPlayerName;

    private readonly string[] regions = { "Random", "Hospital", "Hotel" };
    private readonly string[] difficulties = { "Relaxed", "Standard", "Hard" };

    public static void Show(MainMenu owner)
    {
        if (owner == null) return;

        LobbyMenuMockup mockup = owner.GetComponent<LobbyMenuMockup>();
        if (mockup == null)
            mockup = owner.gameObject.AddComponent<LobbyMenuMockup>();

        mockup.mainMenu = owner;
        mockup.BuildIfNeeded();
        mockup.ShowHome();
        mockup.root.SetActive(true);
    }

    private void Awake()
    {
        mainMenu = GetComponent<MainMenu>();
    }

    private void BuildIfNeeded()
    {
        if (root != null) return;

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("Lobby mockup needs a Canvas in the Menu scene.");
            return;
        }

        root = CreatePanel(canvas.transform, "Lobby Menu", Background);
        Stretch(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);

        GameObject frame = CreatePanel(root.transform, "Lobby Frame", Panel);
        SetRect(frame.GetComponent<RectTransform>(),
            new Vector2(0.08f, 0.08f),
            new Vector2(0.92f, 0.92f),
            Vector2.zero,
            Vector2.zero);

        CreateText(frame.transform, "Brand", "POOLHAUNTERS", 22f,
            new Vector2(0f, 0.9f), new Vector2(1f, 1f),
            new Vector2(30f, 0f), new Vector2(-30f, 0f),
            TextAlignmentOptions.MidlineLeft, Accent);

        Button closeButton = CreateButton(frame.transform, "Close", "X", PanelAlt);
        SetRect(closeButton.GetComponent<RectTransform>(),
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-70f, -64f), new Vector2(-24f, -18f));
        closeButton.onClick.AddListener(Close);

        homePanel = CreateContentPanel(frame.transform, "Lobby Home");
        createPanel = CreateContentPanel(frame.transform, "Create Room");
        joinPanel = CreateContentPanel(frame.transform, "Join Room");
        roomPanel = CreateContentPanel(frame.transform, "Room");

        BuildHome();
        BuildCreate();
        BuildJoin();
        BuildRoom();
    }

    private GameObject CreateContentPanel(Transform parent, string name)
    {
        GameObject content = CreatePanel(parent, name, Background);
        SetRect(content.GetComponent<RectTransform>(),
            new Vector2(0f, 0f), new Vector2(1f, 0.88f),
            new Vector2(24f, 24f), new Vector2(-24f, 0f));
        return content;
    }

    private void BuildHome()
    {
        CreateText(homePanel.transform, "Heading", "MULTIPLAYER", 34f,
            new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.9f),
            Vector2.zero, Vector2.zero,
            TextAlignmentOptions.Center, TextPrimary);

        Button create = CreateButton(homePanel.transform, "Create Room", "CREATE ROOM", Accent);
        SetRect(create.GetComponent<RectTransform>(),
            new Vector2(0.2f, 0.46f), new Vector2(0.8f, 0.61f),
            Vector2.zero, Vector2.zero);
        create.onClick.AddListener(ShowCreate);

        Button join = CreateButton(homePanel.transform, "Join Room", "JOIN ROOM", PanelAlt);
        SetRect(join.GetComponent<RectTransform>(),
            new Vector2(0.2f, 0.27f), new Vector2(0.8f, 0.42f),
            Vector2.zero, Vector2.zero);
        join.onClick.AddListener(ShowJoin);

        CreateText(homePanel.transform, "Status",
            "UP TO 4 PLAYERS", 15f,
            new Vector2(0.2f, 0.13f), new Vector2(0.8f, 0.22f),
            Vector2.zero, Vector2.zero,
            TextAlignmentOptions.Center, TextMuted);
    }

    private void BuildCreate()
    {
        CreateSectionTitle(createPanel.transform, "CREATE ROOM");

        roomNameInput = CreateInput(createPanel.transform, "Room Name", "Room name");
        SetRect(roomNameInput.GetComponent<RectTransform>(),
            new Vector2(0.08f, 0.67f), new Vector2(0.58f, 0.79f),
            Vector2.zero, Vector2.zero);
        roomNameInput.text = "Cleaning Crew";

        hostNameInput = CreateInput(createPanel.transform, "Player Name", "Player name");
        SetRect(hostNameInput.GetComponent<RectTransform>(),
            new Vector2(0.61f, 0.67f), new Vector2(0.92f, 0.79f),
            Vector2.zero, Vector2.zero);
        hostNameInput.text = "Host";

        CreateText(createPanel.transform, "Players Label", "PLAYERS", 14f,
            new Vector2(0.08f, 0.55f), new Vector2(0.28f, 0.63f),
            Vector2.zero, Vector2.zero,
            TextAlignmentOptions.MidlineLeft, TextMuted);

        Button minus = CreateButton(createPanel.transform, "Fewer Players", "-", PanelAlt);
        SetRect(minus.GetComponent<RectTransform>(),
            new Vector2(0.3f, 0.54f), new Vector2(0.37f, 0.64f),
            Vector2.zero, Vector2.zero);
        minus.onClick.AddListener(() => ChangePlayerLimit(-1));

        playerCountText = CreateText(createPanel.transform, "Player Count", "4", 22f,
            new Vector2(0.38f, 0.54f), new Vector2(0.46f, 0.64f),
            Vector2.zero, Vector2.zero,
            TextAlignmentOptions.Center, TextPrimary);

        Button plus = CreateButton(createPanel.transform, "More Players", "+", PanelAlt);
        SetRect(plus.GetComponent<RectTransform>(),
            new Vector2(0.47f, 0.54f), new Vector2(0.54f, 0.64f),
            Vector2.zero, Vector2.zero);
        plus.onClick.AddListener(() => ChangePlayerLimit(1));

        Button privacy = CreateButton(createPanel.transform, "Privacy", "PRIVATE", PanelAlt);
        SetRect(privacy.GetComponent<RectTransform>(),
            new Vector2(0.61f, 0.54f), new Vector2(0.92f, 0.64f),
            Vector2.zero, Vector2.zero);
        privacyText = privacy.GetComponentInChildren<TMP_Text>();
        privacy.onClick.AddListener(TogglePrivacy);

        Button region = CreateButton(createPanel.transform, "Region", "REGION: RANDOM", PanelAlt);
        SetRect(region.GetComponent<RectTransform>(),
            new Vector2(0.08f, 0.39f), new Vector2(0.48f, 0.5f),
            Vector2.zero, Vector2.zero);
        regionText = region.GetComponentInChildren<TMP_Text>();
        region.onClick.AddListener(CycleRegion);

        Button difficulty = CreateButton(createPanel.transform, "Difficulty", "DIFFICULTY: STANDARD", PanelAlt);
        SetRect(difficulty.GetComponent<RectTransform>(),
            new Vector2(0.52f, 0.39f), new Vector2(0.92f, 0.5f),
            Vector2.zero, Vector2.zero);
        difficultyText = difficulty.GetComponentInChildren<TMP_Text>();
        difficulty.onClick.AddListener(CycleDifficulty);

        Button back = CreateButton(createPanel.transform, "Back", "BACK", PanelAlt);
        SetRect(back.GetComponent<RectTransform>(),
            new Vector2(0.08f, 0.12f), new Vector2(0.34f, 0.25f),
            Vector2.zero, Vector2.zero);
        back.onClick.AddListener(ShowHome);

        Button confirm = CreateButton(createPanel.transform, "Confirm", "CREATE", Accent);
        SetRect(confirm.GetComponent<RectTransform>(),
            new Vector2(0.62f, 0.12f), new Vector2(0.92f, 0.25f),
            Vector2.zero, Vector2.zero);
        confirm.onClick.AddListener(CreateRoom);
    }

    private void BuildJoin()
    {
        CreateSectionTitle(joinPanel.transform, "JOIN ROOM");

        joinCodeInput = CreateInput(joinPanel.transform, "Room Code", "Room code");
        SetRect(joinCodeInput.GetComponent<RectTransform>(),
            new Vector2(0.18f, 0.58f), new Vector2(0.82f, 0.72f),
            Vector2.zero, Vector2.zero);
        joinCodeInput.characterLimit = 6;

        joinNameInput = CreateInput(joinPanel.transform, "Player Name", "Player name");
        SetRect(joinNameInput.GetComponent<RectTransform>(),
            new Vector2(0.18f, 0.4f), new Vector2(0.82f, 0.54f),
            Vector2.zero, Vector2.zero);
        joinNameInput.text = "Player";

        Button back = CreateButton(joinPanel.transform, "Back", "BACK", PanelAlt);
        SetRect(back.GetComponent<RectTransform>(),
            new Vector2(0.08f, 0.12f), new Vector2(0.34f, 0.25f),
            Vector2.zero, Vector2.zero);
        back.onClick.AddListener(ShowHome);

        Button join = CreateButton(joinPanel.transform, "Join", "JOIN", Accent);
        SetRect(join.GetComponent<RectTransform>(),
            new Vector2(0.62f, 0.12f), new Vector2(0.92f, 0.25f),
            Vector2.zero, Vector2.zero);
        join.onClick.AddListener(JoinRoom);
    }

    private void BuildRoom()
    {
        roomTitleText = CreateText(roomPanel.transform, "Room Title", "ROOM", 27f,
            new Vector2(0.06f, 0.82f), new Vector2(0.65f, 0.96f),
            Vector2.zero, Vector2.zero,
            TextAlignmentOptions.MidlineLeft, TextPrimary);

        roomCodeText = CreateText(roomPanel.transform, "Room Code", "CODE: ------", 16f,
            new Vector2(0.66f, 0.82f), new Vector2(0.94f, 0.96f),
            Vector2.zero, Vector2.zero,
            TextAlignmentOptions.MidlineRight, Warning);

        GameObject roster = CreatePanel(roomPanel.transform, "Roster", PanelAlt);
        SetRect(roster.GetComponent<RectTransform>(),
            new Vector2(0.06f, 0.25f), new Vector2(0.58f, 0.79f),
            Vector2.zero, Vector2.zero);

        for (int i = 0; i < playerRows.Length; i++)
        {
            float top = 0.94f - i * 0.23f;
            playerRows[i] = CreateText(roster.transform, "Player " + (i + 1),
                "EMPTY SLOT", 16f,
                new Vector2(0.06f, top - 0.18f), new Vector2(0.94f, top),
                Vector2.zero, Vector2.zero,
                TextAlignmentOptions.MidlineLeft, TextMuted);
        }

        roomSettingsText = CreateText(roomPanel.transform, "Settings",
            "", 15f,
            new Vector2(0.63f, 0.45f), new Vector2(0.94f, 0.79f),
            Vector2.zero, Vector2.zero,
            TextAlignmentOptions.TopLeft, TextMuted);

        Button ready = CreateButton(roomPanel.transform, "Ready", "READY", PanelAlt);
        SetRect(ready.GetComponent<RectTransform>(),
            new Vector2(0.63f, 0.3f), new Vector2(0.94f, 0.42f),
            Vector2.zero, Vector2.zero);
        readyButtonText = ready.GetComponentInChildren<TMP_Text>();
        ready.onClick.AddListener(ToggleReady);

        Button leave = CreateButton(roomPanel.transform, "Leave", "LEAVE", Danger);
        SetRect(leave.GetComponent<RectTransform>(),
            new Vector2(0.06f, 0.07f), new Vector2(0.3f, 0.19f),
            Vector2.zero, Vector2.zero);
        leave.onClick.AddListener(LeaveRoom);

        startButton = CreateButton(roomPanel.transform, "Start Run", "START RUN", Accent);
        SetRect(startButton.GetComponent<RectTransform>(),
            new Vector2(0.63f, 0.07f), new Vector2(0.94f, 0.19f),
            Vector2.zero, Vector2.zero);
        startButtonText = startButton.GetComponentInChildren<TMP_Text>();
        startButton.onClick.AddListener(StartRun);
    }

    private void CreateSectionTitle(Transform parent, string title)
    {
        CreateText(parent, "Heading", title, 28f,
            new Vector2(0.08f, 0.83f), new Vector2(0.92f, 0.96f),
            Vector2.zero, Vector2.zero,
            TextAlignmentOptions.MidlineLeft, TextPrimary);
    }

    private void ChangePlayerLimit(int amount)
    {
        maximumPlayers = Mathf.Clamp(maximumPlayers + amount, 2, 4);
        playerCountText.text = maximumPlayers.ToString();
    }

    private void TogglePrivacy()
    {
        isPrivate = !isPrivate;
        privacyText.text = isPrivate ? "PRIVATE" : "PUBLIC";
    }

    private void CycleRegion()
    {
        regionIndex = (regionIndex + 1) % regions.Length;
        regionText.text = "REGION: " + regions[regionIndex].ToUpperInvariant();
    }

    private void CycleDifficulty()
    {
        difficultyIndex = (difficultyIndex + 1) % difficulties.Length;
        difficultyText.text = "DIFFICULTY: " +
            difficulties[difficultyIndex].ToUpperInvariant();
    }

    private void CreateRoom()
    {
        localPlayerIsHost = true;
        localPlayerReady = true;
        currentPlayerName = CleanName(hostNameInput.text, "Host");
        currentRoomCode = GenerateRoomCode();
        ShowRoom(CleanName(roomNameInput.text, "Cleaning Crew"));
    }

    private void JoinRoom()
    {
        localPlayerIsHost = false;
        localPlayerReady = false;
        currentPlayerName = CleanName(joinNameInput.text, "Player");
        currentRoomCode = string.IsNullOrWhiteSpace(joinCodeInput.text)
            ? "DEMO01"
            : joinCodeInput.text.Trim().ToUpperInvariant();
        ShowRoom("Joined Room");
    }

    private void ShowRoom(string title)
    {
        ShowOnly(roomPanel);
        roomTitleText.text = title.ToUpperInvariant();
        roomCodeText.text = "CODE: " + currentRoomCode;
        roomSettingsText.text =
            "REGION\n" + regions[regionIndex].ToUpperInvariant() +
            "\n\nDIFFICULTY\n" + difficulties[difficultyIndex].ToUpperInvariant() +
            "\n\nACCESS\n" + (isPrivate ? "PRIVATE" : "PUBLIC");

        RefreshRoomState();
    }

    private void ToggleReady()
    {
        localPlayerReady = !localPlayerReady;
        RefreshRoomState();
    }

    private void RefreshRoomState()
    {
        playerRows[0].text = currentPlayerName.ToUpperInvariant() +
            (localPlayerIsHost ? "  [HOST]" : "") +
            (localPlayerReady ? "  READY" : "  NOT READY");
        playerRows[0].color = localPlayerReady ? Accent : Warning;

        for (int i = 1; i < playerRows.Length; i++)
        {
            bool available = i < maximumPlayers;
            playerRows[i].text = available ? "EMPTY SLOT" : "CLOSED";
            playerRows[i].color = TextMuted;
        }

        readyButtonText.text = localPlayerReady ? "NOT READY" : "READY";
        startButton.gameObject.SetActive(localPlayerIsHost);
        startButton.interactable = localPlayerReady;
        startButtonText.text = localPlayerReady ? "START RUN" : "WAITING";
    }

    private void StartRun()
    {
        if (!localPlayerIsHost || !localPlayerReady || mainMenu == null)
            return;

        mainMenu.StartGameFromLobby(regions[regionIndex]);
    }

    private void LeaveRoom()
    {
        localPlayerReady = false;
        localPlayerIsHost = false;
        currentRoomCode = null;
        ShowHome();
    }

    private void Close()
    {
        if (root != null)
            root.SetActive(false);
    }

    private void ShowHome()
    {
        BuildIfNeeded();
        ShowOnly(homePanel);
    }

    private void ShowCreate()
    {
        ShowOnly(createPanel);
    }

    private void ShowJoin()
    {
        ShowOnly(joinPanel);
    }

    private void ShowOnly(GameObject selected)
    {
        homePanel.SetActive(selected == homePanel);
        createPanel.SetActive(selected == createPanel);
        joinPanel.SetActive(selected == joinPanel);
        roomPanel.SetActive(selected == roomPanel);
    }

    private string GenerateRoomCode()
    {
        const string characters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        char[] code = new char[6];
        for (int i = 0; i < code.Length; i++)
            code[i] = characters[UnityEngine.Random.Range(0, characters.Length)];
        return new string(code);
    }

    private string CleanName(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static GameObject CreatePanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = color;
        return panel;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string name,
        string value,
        float size,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        TextAlignmentOptions alignment,
        Color color)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        SetRect(rect, anchorMin, anchorMax, offsetMin, offsetMax);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        return text;
    }

    private static Button CreateButton(
        Transform parent,
        string name,
        string label,
        Color color)
    {
        GameObject buttonObject = new GameObject(name,
            typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.12f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.18f);
        colors.disabledColor = new Color(color.r, color.g, color.b, 0.35f);
        button.colors = colors;

        CreateText(buttonObject.transform, "Label", label, 15f,
            Vector2.zero, Vector2.one,
            new Vector2(10f, 4f), new Vector2(-10f, -4f),
            TextAlignmentOptions.Center, TextPrimary);
        return button;
    }

    private static TMP_InputField CreateInput(
        Transform parent,
        string name,
        string placeholderValue)
    {
        GameObject inputObject = new GameObject(name,
            typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        inputObject.transform.SetParent(parent, false);
        inputObject.GetComponent<Image>().color = PanelAlt;

        GameObject textArea = new GameObject("Text Area", typeof(RectTransform));
        textArea.transform.SetParent(inputObject.transform, false);
        Stretch(textArea.GetComponent<RectTransform>(),
            new Vector2(14f, 6f), new Vector2(-14f, -6f));

        TMP_Text placeholder = CreateText(textArea.transform, "Placeholder",
            placeholderValue.ToUpperInvariant(), 15f,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            TextAlignmentOptions.MidlineLeft, TextMuted);

        TMP_Text valueText = CreateText(textArea.transform, "Text", "", 16f,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            TextAlignmentOptions.MidlineLeft, TextPrimary);

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.textViewport = textArea.GetComponent<RectTransform>();
        input.textComponent = valueText;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }

    private static void Stretch(
        RectTransform rect,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        SetRect(rect, Vector2.zero, Vector2.one, offsetMin, offsetMax);
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        rect.localScale = Vector3.one;
    }
}
