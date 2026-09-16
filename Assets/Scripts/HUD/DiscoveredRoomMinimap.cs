using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class DiscoveredRoomMinimap : MonoBehaviour
{
    [Header("References")]
    public LevelObjectiveManager objectiveManager;
    public RectTransform mapRoot;
    public RectTransform playerMarker;

    [Header("Room Markers")]
    public Image roomMarkerPrefab;
    public Vector2 roomMarkerSize = new Vector2(16f, 16f);
    public float worldToMapScale = 2f;
    public Color startRoomColor = new Color(0.25f, 1f, 0.45f, 0.9f);
    public Color normalRoomColor = new Color(0.75f, 0.9f, 1f, 0.85f);
    public Color waterRoomColor = new Color(0.25f, 0.65f, 1f, 0.9f);
    public Color finalRoomColor = new Color(1f, 0.35f, 0.35f, 0.95f);
    public Color currentRoomColor = new Color(1f, 0.95f, 0.35f, 1f);

    [Header("Behavior")]
    public bool autoBindObjectiveManager = true;
    public bool centerOnStartRoom = true;
    public bool updatePlayerMarker = true;
    public bool rotateWithPlayer = false;

    private readonly Dictionary<RoomDefinition, Image> roomMarkers = new Dictionary<RoomDefinition, Image>();
    private readonly Dictionary<RoomDefinition, Color> roomBaseColors = new Dictionary<RoomDefinition, Color>();
    private Sprite playerDotSprite;
    private Texture2D playerDotTexture;
    private Vector3 mapOrigin;
    private RoomDefinition currentRoom;

    void Awake()
    {
        if (mapRoot == null)
            mapRoot = transform as RectTransform;
    }

    void OnEnable()
    {
        BindIfNeeded();

        if (objectiveManager != null)
        {
            objectiveManager.OnRoomDiscovered += HandleRoomDiscovered;
            RebuildFromDiscoveredRooms();
        }
    }

    void OnDisable()
    {
        if (objectiveManager != null)
            objectiveManager.OnRoomDiscovered -= HandleRoomDiscovered;
    }

    private PlayerStatus[] mapPlayers = new PlayerStatus[0];
    private float nextPlayerRefresh;
    private readonly Dictionary<PlayerStatus, RectTransform> teammateMarkers = new Dictionary<PlayerStatus, RectTransform>();
    private readonly List<PlayerStatus> staleMarkers = new List<PlayerStatus>();

    void Update()
    {
        if (objectiveManager == null)
        {
            BindIfNeeded();
            if (objectiveManager != null)
            { objectiveManager.OnRoomDiscovered += HandleRoomDiscovered; RebuildFromDiscoveredRooms(); }
        }
        if (Time.unscaledTime >= nextPlayerRefresh)
        {
            nextPlayerRefresh = Time.unscaledTime + 0.5f;
            mapPlayers = FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None);
        }
        UpdateTeammates();
        if (updatePlayerMarker)
            UpdatePlayerMarker();

        UpdateCurrentRoomHighlight();
    }

    void BindIfNeeded()
    {
        if (!autoBindObjectiveManager || objectiveManager != null) return;
        objectiveManager = LevelObjectiveManager.Instance;
    }

    void RebuildFromDiscoveredRooms()
    {
        if (objectiveManager == null) return;

        IReadOnlyList<RoomDefinition> rooms = objectiveManager.DiscoveredRooms;
        for (int i = 0; i < rooms.Count; i++)
            AddRoomMarker(rooms[i], i);
    }

    void HandleRoomDiscovered(RoomDefinition room, int discoveryIndex)
    {
        AddRoomMarker(room, discoveryIndex);
    }

    void AddRoomMarker(RoomDefinition room, int discoveryIndex)
    {
        if (room == null || roomMarkers.ContainsKey(room)) return;
        if (mapRoot == null) return;

        if (roomMarkers.Count == 0 && centerOnStartRoom)
            mapOrigin = room.GetWorldBounds().center;

        Image marker = CreateMarker();
        marker.rectTransform.SetParent(mapRoot, false);
        marker.rectTransform.sizeDelta = GetMarkerSize(room);
        marker.rectTransform.anchoredPosition = WorldToMap(room.GetWorldBounds().center);

        Color color = GetRoomColor(room, discoveryIndex);
        marker.color = color;
        roomMarkers.Add(room, marker);
        roomBaseColors.Add(room, color);
    }

    Image CreateMarker()
    {
        if (roomMarkerPrefab != null)
            return Instantiate(roomMarkerPrefab);

        GameObject markerObject = new GameObject("RoomMarker", typeof(RectTransform), typeof(Image));
        Image marker = markerObject.GetComponent<Image>();
        marker.raycastTarget = false;
        return marker;
    }

    Vector2 GetMarkerSize(RoomDefinition room)
    {
        if (room == null) return roomMarkerSize;

        Vector3 size = room.GetWorldBounds().size;
        return new Vector2(
            Mathf.Max(4f, size.x * worldToMapScale),
            Mathf.Max(4f, size.z * worldToMapScale));
    }

    Vector2 WorldToMap(Vector3 worldPosition)
    {
        Vector3 local = worldPosition - mapOrigin;
        return new Vector2(local.x, local.z) * worldToMapScale;
    }

    Color GetRoomColor(RoomDefinition room, int discoveryIndex)
    {
        if (discoveryIndex == 0 || room.category == RoomCategory.SubmarineSpawn)
            return startRoomColor;

        if (room.category == RoomCategory.Final)
            return finalRoomColor;

        if (room.category == RoomCategory.Water || room.category == RoomCategory.Pool)
            return waterRoomColor;

        return normalRoomColor;
    }

    void UpdatePlayerMarker()
    {
        if (playerMarker == null) return;

        PlayerStatus player = FindLocalPlayerForMap();
        if (player == null)
        {
            playerMarker.gameObject.SetActive(false);
            return;
        }

        StylePlayerDot(playerMarker, player);
        playerMarker.gameObject.SetActive(true);
        playerMarker.anchoredPosition = WorldToMap(player.transform.position);

        if (rotateWithPlayer)
            playerMarker.localRotation = Quaternion.Euler(0f, 0f, -player.transform.eulerAngles.y);
    }

    PlayerStatus FindLocalPlayerForMap()
    {
        bool online = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        foreach (var player in mapPlayers)
            if (player != null && !player.IsDead() && (!online || (player.IsSpawned && player.IsOwner))) return player;
        return null;
    }

    RectTransform CreatePlayerDot(string name, Color color)
    {
        var dot = new GameObject(name, typeof(RectTransform), typeof(Image));
        dot.transform.SetParent(mapRoot, false);
        var image = dot.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        image.rectTransform.sizeDelta = new Vector2(5f, 5f);
        return image.rectTransform;
    }

    void StylePlayerDot(RectTransform marker, PlayerStatus player)
    {
        if (playerDotSprite == null)
        {
            const int size = 32;
            playerDotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            playerDotTexture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(15.5f - distance));
                }
            playerDotTexture.SetPixels(pixels);
            playerDotTexture.Apply(false, true);
            playerDotSprite = Sprite.Create(playerDotTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
        var image = marker.GetComponent<Image>();
        if (image == null) image = marker.gameObject.AddComponent<Image>();
        image.sprite = playerDotSprite;
        image.overrideSprite = null;
        image.type = Image.Type.Simple;
        image.material = null;
        image.raycastTarget = false;
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.sizeDelta = new Vector2(5f, 5f);
        marker.localScale = Vector3.one;
        var loadout = player.GetComponent<PlayerAgentLoadout>();
        var agent = loadout != null ? loadout.currentAgent : PlayerAgentType.JennyPie;
        switch (agent)
        {
            case PlayerAgentType.Sylvian: image.color = new Color(0.3f, 0.85f, 1f); break;
            case PlayerAgentType.SecretAgent: image.color = new Color(1f, 0.8f, 0.2f); break;
            case PlayerAgentType.Louise: image.color = new Color(0.65f, 0.45f, 1f); break;
            default: image.color = new Color(1f, 0.4f, 0.65f); break;
        }
    }

    void OnDestroy()
    {
        if (playerDotSprite != null) Destroy(playerDotSprite);
        if (playerDotTexture != null) Destroy(playerDotTexture);
    }

    void UpdateTeammates()
    {
        if (!updatePlayerMarker || mapRoot == null) return;
        var local = FindLocalPlayerForMap();
        if (playerMarker == null) playerMarker = CreatePlayerDot("Local Player", new Color(0.3f,1f,0.45f));
        bool online = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        foreach (var player in mapPlayers)
        {
            if (player == null || player == local || player.IsDead() || (online && !player.IsSpawned)) continue;
            if (!teammateMarkers.TryGetValue(player, out var marker))
            { marker = CreatePlayerDot("Teammate", new Color(0.25f,0.65f,1f)); teammateMarkers.Add(player, marker); }
            StylePlayerDot(marker, player);
            marker.anchoredPosition = WorldToMap(player.transform.position);
            marker.gameObject.SetActive(true); marker.SetAsLastSibling();
        }
        staleMarkers.Clear();
        foreach (var pair in teammateMarkers)
            if (pair.Key == null || pair.Key == local || pair.Key.IsDead() || System.Array.IndexOf(mapPlayers, pair.Key) < 0)
            { if (pair.Value != null) Destroy(pair.Value.gameObject); staleMarkers.Add(pair.Key); }
        foreach (var player in staleMarkers) teammateMarkers.Remove(player);
        playerMarker.SetAsLastSibling();
    }

    void UpdateCurrentRoomHighlight()
    {
        RoomDefinition nextCurrentRoom = null;
        var player = FindLocalPlayerForMap();
        if (player != null)
            foreach (RoomDefinition room in roomMarkers.Keys)
            {
                if (room == null) continue;
                Bounds bounds = room.GetWorldBounds(); bounds.Expand(0.5f);
                if (bounds.Contains(player.transform.position)) { nextCurrentRoom = room; break; }
            }

        if (nextCurrentRoom == currentRoom) return;

        if (currentRoom != null && roomMarkers.ContainsKey(currentRoom))
            roomMarkers[currentRoom].color = roomBaseColors[currentRoom];

        currentRoom = nextCurrentRoom;

        if (currentRoom != null && roomMarkers.ContainsKey(currentRoom))
            roomMarkers[currentRoom].color = currentRoomColor;
    }
}
