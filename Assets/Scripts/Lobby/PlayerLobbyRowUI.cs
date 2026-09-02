using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PlayerLobbyRowUI : MonoBehaviour
{
    const float RowHeight = 42f;
    const float HorizontalPadding = 12f;
    const float StatusWidth = 140f;
    const float ColumnGap = 8f;

    [Header("Text")]
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private TMP_Text statusText;

    [Header("Optional Visuals")]
    [SerializeField] private GameObject hostBadge;
    [SerializeField] private Graphic rowBackground;
    [SerializeField] private Graphic statusGraphic;

    [Header("Labels")]
    [SerializeField] private string readyLabel = "Ready";
    [SerializeField] private string notReadyLabel = "Not Ready";
    [SerializeField] private string localPlayerSuffix = " (You)";

    [Header("Colors")]
    [SerializeField] private Color normalRowColor = new Color(1f, 1f, 1f, 0.08f);
    [SerializeField] private Color localRowColor = new Color(0.2f, 0.75f, 1f, 0.18f);
    [SerializeField] private Color hostStatusColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField] private Color readyStatusColor = new Color(0.3f, 1f, 0.55f, 1f);
    [SerializeField] private Color notReadyStatusColor = new Color(1f, 0.45f, 0.35f, 1f);

    public void SetPlayer(
        string playerName,
        bool isHost,
        bool isReady,
        bool isLocalPlayer)
    {
        ResolveReferences();
        ConfigureLayout();

        string safeName = string.IsNullOrWhiteSpace(playerName)
            ? "Player"
            : playerName.Trim();

        string status = GetStatusLabel(isHost, isReady);
        if (playerNameText != null)
        {
            string displayName = isLocalPlayer ? safeName + localPlayerSuffix : safeName;
            playerNameText.text = displayName;
        }

        if (statusText != null)
        {
            statusText.gameObject.SetActive(true);
            statusText.text = status;
            statusText.color = GetStatusColor(isHost, isReady);
        }

        if (hostBadge != null)
            hostBadge.SetActive(isHost);

        if (rowBackground != null)
            rowBackground.color = isLocalPlayer ? localRowColor : normalRowColor;

        if (statusGraphic != null)
            statusGraphic.color = GetStatusColor(isHost, isReady);

        if (statusText != null)
            statusText.color = GetStatusColor(isHost, isReady);
    }

    void Awake()
    {
        ResolveReferences();
        ConfigureLayout();
    }

    void OnValidate()
    {
        ResolveReferences();
        ConfigureLayout();
    }

    string GetStatusLabel(bool isHost, bool isReady)
    {
        return isReady ? readyLabel : notReadyLabel;
    }

    Color GetStatusColor(bool isHost, bool isReady)
    {
        if (!isReady)
            return notReadyStatusColor;

        return isHost ? hostStatusColor : readyStatusColor;
    }

    void ResolveReferences()
    {
        if (playerNameText == null)
            playerNameText = FindTextByName("PlayerName", "Playername", "PlayerNameText");

        if (statusText == null)
            statusText = FindTextByName("Status", "StatusText", "ReadyStatusText");

        if (rowBackground == null)
            rowBackground = GetComponent<Graphic>();
    }

    void ConfigureLayout()
    {
        RectTransform rowRect = transform as RectTransform;
        if (rowRect != null)
        {
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.offsetMin = new Vector2(0f, rowRect.offsetMin.y);
            rowRect.offsetMax = new Vector2(0f, rowRect.offsetMax.y);
            rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, RowHeight);
        }

        LayoutElement layoutElement = GetComponent<LayoutElement>();
        if (layoutElement == null && Application.isPlaying)
            layoutElement = gameObject.AddComponent<LayoutElement>();

        if (layoutElement != null)
        {
            layoutElement.ignoreLayout = false;
            layoutElement.minHeight = RowHeight;
            layoutElement.preferredHeight = RowHeight;
            layoutElement.flexibleHeight = 0f;
            layoutElement.flexibleWidth = 1f;
        }

        HorizontalLayoutGroup horizontalLayout = GetComponent<HorizontalLayoutGroup>();
        if (horizontalLayout != null)
            horizontalLayout.enabled = false;

        ConfigureNameTextLayout();
        ConfigureStatusTextLayout();
    }

    void ConfigureNameTextLayout()
    {
        if (playerNameText == null)
            return;

        RectTransform rect = playerNameText.transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(HorizontalPadding, 0f);
            rect.offsetMax = new Vector2(-(StatusWidth + HorizontalPadding + ColumnGap), 0f);
        }

        LayoutElement layoutElement = playerNameText.GetComponent<LayoutElement>();
        if (layoutElement == null && Application.isPlaying)
            layoutElement = playerNameText.gameObject.AddComponent<LayoutElement>();

        if (layoutElement != null)
        {
            layoutElement.ignoreLayout = false;
            layoutElement.minWidth = 0f;
            layoutElement.preferredWidth = 0f;
            layoutElement.flexibleWidth = 1f;
            layoutElement.minHeight = RowHeight;
            layoutElement.preferredHeight = RowHeight;
        }

        playerNameText.alignment = TextAlignmentOptions.Left;
        playerNameText.textWrappingMode = TextWrappingModes.NoWrap;
        playerNameText.overflowMode = TextOverflowModes.Ellipsis;
        playerNameText.fontSize = Mathf.Min(playerNameText.fontSize, 22f);
    }

    void ConfigureStatusTextLayout()
    {
        if (statusText == null)
            return;

        statusText.gameObject.SetActive(true);

        RectTransform rect = statusText.transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-HorizontalPadding, 0f);
            rect.sizeDelta = new Vector2(StatusWidth, 0f);
        }

        LayoutElement layoutElement = statusText.GetComponent<LayoutElement>();
        if (layoutElement == null && Application.isPlaying)
            layoutElement = statusText.gameObject.AddComponent<LayoutElement>();

        if (layoutElement != null)
        {
            layoutElement.ignoreLayout = true;
            layoutElement.minWidth = StatusWidth;
            layoutElement.preferredWidth = StatusWidth;
            layoutElement.flexibleWidth = 0f;
            layoutElement.minHeight = RowHeight;
            layoutElement.preferredHeight = RowHeight;
        }

        statusText.alignment = TextAlignmentOptions.Right;
        statusText.textWrappingMode = TextWrappingModes.NoWrap;
        statusText.overflowMode = TextOverflowModes.Ellipsis;
        statusText.fontSize = Mathf.Min(statusText.fontSize, 22f);
    }

    TMP_Text FindTextByName(params string[] names)
    {
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < names.Length; i++)
        {
            for (int j = 0; j < texts.Length; j++)
            {
                if (texts[j] != null && texts[j].gameObject.name == names[i])
                    return texts[j];
            }
        }

        return null;
    }
}
