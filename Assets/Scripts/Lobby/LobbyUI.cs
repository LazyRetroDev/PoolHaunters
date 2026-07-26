using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class LobbyUI : MonoBehaviour
{
    public struct PlayerView
    {
        public ulong clientId;
        public string playerName;
        public bool isHost;
        public bool isReady;
        public bool isLocalPlayer;

        public PlayerView(
            ulong clientId,
            string playerName,
            bool isHost,
            bool isReady,
            bool isLocalPlayer)
        {
            this.clientId = clientId;
            this.playerName = playerName;
            this.isHost = isHost;
            this.isReady = isReady;
            this.isLocalPlayer = isLocalPlayer;
        }
    }

    [Header("Player List")]
    [SerializeField] private ScrollRect playerList;
    [SerializeField] private Transform playerListContent;
    [SerializeField] private GameObject playerRowPrefab;
    [SerializeField] private GameObject emptyListMessage;

    [Header("Lobby State")]
    [SerializeField] private TMP_Text joinCodeText;
    [SerializeField] private TMP_Text lobbyStatusText;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startGameButton;

    private readonly Dictionary<ulong, PlayerLobbyRowUI> rowsByClientId =
        new Dictionary<ulong, PlayerLobbyRowUI>();

    public void SetJoinCode(string joinCode)
    {
        if (joinCodeText == null)
            return;

        joinCodeText.text = string.IsNullOrWhiteSpace(joinCode)
            ? string.Empty
            : joinCode.Trim().ToUpperInvariant();
    }

    public void SetStatus(string status)
    {
        if (lobbyStatusText != null)
            lobbyStatusText.text = string.IsNullOrWhiteSpace(status)
                ? string.Empty
                : status;
    }

    public void SetReadyButtonInteractable(bool interactable)
    {
        if (readyButton != null)
            readyButton.interactable = interactable;
    }

    public void SetStartGameButtonInteractable(bool interactable)
    {
        if (startGameButton != null)
            startGameButton.interactable = interactable;
    }

    public void SetPlayers(IReadOnlyList<PlayerView> players)
    {
        ClearPlayers();

        if (players == null)
        {
            RefreshEmptyState();
            return;
        }

        for (int i = 0; i < players.Count; i++)
            UpsertPlayer(players[i]);

        RebuildPlayerListLayout();
        RefreshEmptyState();
    }

    public void UpsertPlayer(PlayerView player)
    {
        ResolveReferences();

        if (playerListContent == null || playerRowPrefab == null)
        {
            Debug.LogWarning("LobbyUI cannot create player rows because Content or Player Row Prefab is missing.");
            return;
        }

        if (!rowsByClientId.TryGetValue(player.clientId, out PlayerLobbyRowUI row) || row == null)
        {
            GameObject rowObject = Instantiate(playerRowPrefab, playerListContent);
            rowObject.SetActive(true);

            row = rowObject.GetComponent<PlayerLobbyRowUI>();
            if (row == null)
                row = rowObject.AddComponent<PlayerLobbyRowUI>();

            rowsByClientId[player.clientId] = row;
        }

        row.SetPlayer(
            player.playerName,
            player.isHost,
            player.isReady,
            player.isLocalPlayer);

        RebuildPlayerListLayout();
        RefreshEmptyState();
    }

    public void RemovePlayer(ulong clientId)
    {
        if (!rowsByClientId.TryGetValue(clientId, out PlayerLobbyRowUI row))
            return;

        rowsByClientId.Remove(clientId);

        if (row != null)
            DestroyRow(row.gameObject);

        RefreshEmptyState();
    }

    public void ClearPlayers()
    {
        ResolveReferences();

        HashSet<PlayerLobbyRowUI> rowsToDestroy =
            new HashSet<PlayerLobbyRowUI>(rowsByClientId.Values);

        if (playerListContent != null)
        {
            PlayerLobbyRowUI[] childRows =
                playerListContent.GetComponentsInChildren<PlayerLobbyRowUI>(true);
            for (int i = 0; i < childRows.Length; i++)
            {
                if (childRows[i] != null)
                    rowsToDestroy.Add(childRows[i]);
            }
        }

        foreach (PlayerLobbyRowUI row in rowsToDestroy)
        {
            if (row != null)
                DestroyRow(row.gameObject);
        }

        rowsByClientId.Clear();
        RebuildPlayerListLayout();
        RefreshEmptyState();
    }

    [ContextMenu("Preview Lobby Players")]
    void PreviewLobbyPlayers()
    {
        SetPlayers(new[]
        {
            new PlayerView(0, "Host Player", true, true, true),
            new PlayerView(1, "Guest Player", false, false, false)
        });
    }

    [ContextMenu("Clear Lobby Players")]
    void ClearLobbyPlayersFromContextMenu()
    {
        ClearPlayers();
    }

    void Awake()
    {
        ResolveReferences();
        RefreshEmptyState();
    }

    void OnValidate()
    {
        ResolveReferences();
    }

    void ResolveReferences()
    {
        if (playerList == null)
        {
            Transform playerListTransform = FindChildByName(transform, "PlayerList");
            if (playerListTransform != null)
                playerList = playerListTransform.GetComponent<ScrollRect>();
        }

        if (playerList == null)
            playerList = GetComponentInChildren<ScrollRect>(true);

        if (playerListContent == null && playerList != null)
            playerListContent = playerList.content;

        if (playerListContent == null)
        {
            Transform content = FindChildByName(transform, "Content");
            if (content != null)
                playerListContent = content;
        }
    }

    void RefreshEmptyState()
    {
        if (emptyListMessage != null)
            emptyListMessage.SetActive(rowsByClientId.Count == 0);
    }

    void RebuildPlayerListLayout()
    {
        if (playerListContent == null)
            return;

        RectTransform contentRect = playerListContent as RectTransform;
        if (contentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
    }

    void DestroyRow(GameObject rowObject)
    {
        if (rowObject == null)
            return;

        if (Application.isPlaying)
            Destroy(rowObject);
        else
            DestroyImmediate(rowObject);
    }
    static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
                return child;

            Transform match = FindChildByName(child, childName);
            if (match != null)
                return match;
        }

        return null;
    }
}
