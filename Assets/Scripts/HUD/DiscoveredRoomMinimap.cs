using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

    void Update()
    {
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

        playerMarker.gameObject.SetActive(true);
        playerMarker.anchoredPosition = WorldToMap(player.transform.position);

        if (rotateWithPlayer)
            playerMarker.localRotation = Quaternion.Euler(0f, 0f, -player.transform.eulerAngles.y);
    }

    PlayerStatus FindLocalPlayerForMap()
    {
        if (objectiveManager != null && objectiveManager.trackedPlayers != null)
        {
            for (int i = 0; i < objectiveManager.trackedPlayers.Length; i++)
            {
                PlayerStatus player = objectiveManager.trackedPlayers[i];
                if (player != null && !player.IsDead())
                    return player;
            }
        }

        PlayerStatus[] players =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && !players[i].IsDead())
                return players[i];
        }

        return null;
    }

    void UpdateCurrentRoomHighlight()
    {
        if (objectiveManager == null || objectiveManager.trackedPlayers == null) return;

        RoomDefinition nextCurrentRoom = null;
        for (int i = 0; i < objectiveManager.trackedPlayers.Length && nextCurrentRoom == null; i++)
        {
            PlayerStatus player = objectiveManager.trackedPlayers[i];
            if (player == null || player.IsDead()) continue;

            foreach (RoomDefinition room in roomMarkers.Keys)
            {
                if (room == null) continue;
                Bounds bounds = room.GetWorldBounds();
                bounds.Expand(0.5f);
                if (bounds.Contains(player.transform.position))
                {
                    nextCurrentRoom = room;
                    break;
                }
            }
        }

        if (nextCurrentRoom == currentRoom) return;

        if (currentRoom != null && roomMarkers.ContainsKey(currentRoom))
            roomMarkers[currentRoom].color = roomBaseColors[currentRoom];

        currentRoom = nextCurrentRoom;

        if (currentRoom != null && roomMarkers.ContainsKey(currentRoom))
            roomMarkers[currentRoom].color = currentRoomColor;
    }
}
