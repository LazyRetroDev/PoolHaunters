using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class RoomResourceSpawner : MonoBehaviour
{
    struct ResourceSelection
    {
        public string label;
        public GameObject prefab;
        public RoomResourceCategory category;

        public ResourceSelection(ResourceEntry entry)
        {
            label = entry != null ? entry.label : string.Empty;
            prefab = entry != null ? entry.prefab : null;
            category = entry != null
                ? entry.category
                : RoomResourceCategory.None;
        }

        public ResourceSelection(RoomContentProfile.ResourceEntry entry)
        {
            label = entry != null ? entry.label : string.Empty;
            prefab = entry != null ? entry.prefab : null;
            category = entry != null
                ? entry.category
                : RoomResourceCategory.None;
        }
    }

    [Serializable]
    public class ResourceEntry
    {
        public string label;
        public GameObject prefab;
        public RoomResourceCategory category = RoomResourceCategory.Pickup;

        [Min(0f)]
        public float weight = 1f;

        [Min(0)]
        public int minimumRoomIndex;

        [Tooltip("Use -1 for no maximum.")]
        public int maximumRoomIndex = -1;

        public bool IsAvailableForRoom(int roomIndex)
        {
            if (prefab == null || weight <= 0f) return false;
            if (roomIndex < minimumRoomIndex) return false;
            return maximumRoomIndex < 0 || roomIndex <= maximumRoomIndex;
        }
    }

    [Header("Spawn Table")]
    public ResourceEntry[] resources;

    [Header("Randomization")]
    public int seedOffset = 1847;

    [Range(0f, 2f)]
    public float spawnChanceMultiplier = 1f;

    [Header("Missing Spawn Point Fallback")]
    public bool useFallbackSpawnPoints = true;

    [Min(1)]
    public int fallbackAttemptsPerRoom = 2;

    [Range(0f, 1f)]
    public float fallbackSpawnChance = 0.65f;

    public RoomResourceCategory fallbackAllowedCategories =
        RoomResourceCategory.All;

    [Min(0f)]
    public float fallbackEdgePadding = 1.25f;

    [Min(0f)]
    public float fallbackFloorOffset = 0.08f;

    public LayerMask fallbackGroundLayers = ~0;

    [Header("Ground Placement")]
    public bool snapSpawnedResourcesToGround = true;

    [Min(0f)]
    public float groundSnapRaycastHeight = 2f;

    [Min(0.1f)]
    public float groundSnapRaycastDistance = 8f;

    [Min(0f)]
    public float groundContactOffset = 0.02f;

    public LayerMask groundSnapLayers = ~0;

    public bool requireGroundInSameRoom = true;

    [Header("Multiplayer")]
    [Tooltip("During a network session, resource prefabs must have a NetworkObject and be registered with NetworkManager.")]
    public bool requireNetworkObjectOnline = true;

    [Tooltip("Warn when a selected online prefab cannot be network-spawned.")]
    public bool logInvalidNetworkPrefabs = true;

    private readonly Dictionary<GameObject, List<GameObject>> resourcesByRoom =
        new Dictionary<GameObject, List<GameObject>>();

    public void SpawnResourcesForRoom(GameObject room, int roomIndex, int runSeed)
    {
        if (room == null || !CanSpawnAuthoritatively()) return;

        RoomDefinition definition = GetRoomDefinition(room);
        RoomContentProfile contentProfile =
            definition != null ? definition.contentProfile : null;
        RoomResourceSpawnPoint[] spawnPoints =
            room.GetComponentsInChildren<RoomResourceSpawnPoint>(true);

        System.Random random = new System.Random(
            CreateRoomSeed(runSeed, roomIndex));
        List<GameObject> spawnedResources = new List<GameObject>();
        float effectiveSpawnChanceMultiplier =
            GetEffectiveSpawnChanceMultiplier(contentProfile);

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            if (useFallbackSpawnPoints)
            {
                SpawnFallbackResources(
                    room,
                    definition,
                    contentProfile,
                    roomIndex,
                    random,
                    effectiveSpawnChanceMultiplier,
                    spawnedResources);
            }

            if (spawnedResources.Count > 0)
                resourcesByRoom[room] = spawnedResources;
            return;
        }

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            RoomResourceSpawnPoint point = spawnPoints[i];
            if (point == null) continue;

            float chance = Mathf.Clamp01(
                point.spawnChance * effectiveSpawnChanceMultiplier);
            if (random.NextDouble() > chance) continue;

            ResourceSelection selected;
            if (!TryChooseResource(
                contentProfile,
                point.allowedCategories,
                roomIndex,
                random,
                out selected))
            {
                continue;
            }

            point.GetSpawnPose(out Vector3 position, out Quaternion rotation);
            GameObject instance = SpawnResource(selected, position, rotation, room);
            if (instance != null)
                spawnedResources.Add(instance);
        }

        if (spawnedResources.Count > 0)
            resourcesByRoom[room] = spawnedResources;
    }

    public void DespawnResourcesForRoom(GameObject room)
    {
        if (room == null || !resourcesByRoom.TryGetValue(room, out List<GameObject> spawned))
            return;

        if (CanSpawnAuthoritatively())
        {
            for (int i = 0; i < spawned.Count; i++)
                DespawnResource(spawned[i]);
        }

        resourcesByRoom.Remove(room);
    }

    void SpawnFallbackResources(
        GameObject room,
        RoomDefinition definition,
        RoomContentProfile contentProfile,
        int roomIndex,
        System.Random random,
        float effectiveSpawnChanceMultiplier,
        List<GameObject> spawnedResources)
    {
        if (definition == null)
        {
            Debug.LogWarning(
                room.name +
                " has no resource spawn points or RoomDefinition for fallback placement.");
            return;
        }

        int attempts = Mathf.Max(1, fallbackAttemptsPerRoom);
        for (int i = 0; i < attempts; i++)
        {
            if (random.NextDouble() > fallbackSpawnChance *
                effectiveSpawnChanceMultiplier)
            {
                continue;
            }

            ResourceSelection selected;
            if (!TryChooseResource(
                contentProfile,
                fallbackAllowedCategories,
                roomIndex,
                random,
                out selected))
            {
                continue;
            }

            Vector3 position;
            Quaternion rotation;
            if (!TryGetFallbackSpawnPose(
                definition,
                random,
                out position,
                out rotation))
            {
                continue;
            }

            GameObject instance = SpawnResource(
                selected,
                position,
                rotation,
                room);
            if (instance != null)
                spawnedResources.Add(instance);
        }
    }

    bool TryGetFallbackSpawnPose(
        RoomDefinition definition,
        System.Random random,
        out Vector3 position,
        out Quaternion rotation)
    {
        Vector3 size = definition.size;
        float halfX = Mathf.Max(0f, size.x * 0.5f - fallbackEdgePadding);
        float halfZ = Mathf.Max(0f, size.z * 0.5f - fallbackEdgePadding);
        float localX = Mathf.Lerp(
            -halfX,
            halfX,
            (float)random.NextDouble());
        float localZ = Mathf.Lerp(
            -halfZ,
            halfZ,
            (float)random.NextDouble());

        Vector3 localTop = definition.boundsCenter +
            new Vector3(localX, size.y * 0.5f + 1f, localZ);
        Vector3 rayOrigin = definition.transform.TransformPoint(localTop);
        Vector3 down = -definition.transform.up;
        float rayDistance = Mathf.Max(3f, size.y + 3f);
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            down,
            rayDistance,
            fallbackGroundLayers,
            QueryTriggerInteraction.Ignore);

        bool foundFloor = false;
        RaycastHit floorHit = new RaycastHit();
        float lowestHeight = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null ||
                !hit.collider.transform.IsChildOf(definition.transform))
            {
                continue;
            }

            if (Vector3.Dot(hit.normal, definition.transform.up) < 0.5f)
                continue;

            float height = Vector3.Dot(
                hit.point,
                definition.transform.up);
            if (height >= lowestHeight)
                continue;

            lowestHeight = height;
            floorHit = hit;
            foundFloor = true;
        }

        if (foundFloor)
        {
            position = floorHit.point +
                definition.transform.up * fallbackFloorOffset;
        }
        else
        {
            Vector3 localFloor = definition.boundsCenter +
                new Vector3(
                    localX,
                    -size.y * 0.5f + fallbackFloorOffset,
                    localZ);
            position = definition.transform.TransformPoint(localFloor);
        }

        float yaw = (float)random.NextDouble() * 360f;
        rotation = Quaternion.AngleAxis(yaw, definition.transform.up) *
            definition.transform.rotation;
        return true;
    }

    bool TryChooseResource(
        RoomContentProfile contentProfile,
        RoomResourceCategory allowedCategories,
        int roomIndex,
        System.Random random,
        out ResourceSelection selected)
    {
        selected = new ResourceSelection();

        if (contentProfile != null && contentProfile.HasResourceTable)
        {
            RoomContentProfile.ResourceEntry contentEntry;
            if (contentProfile.TryChooseResource(
                allowedCategories,
                roomIndex,
                random,
                out contentEntry))
            {
                selected = new ResourceSelection(contentEntry);
                return selected.prefab != null;
            }

            return false;
        }

        ResourceEntry resourceEntry;
        if (TryChooseGlobalResource(
            allowedCategories,
            roomIndex,
            random,
            out resourceEntry))
        {
            selected = new ResourceSelection(resourceEntry);
            return selected.prefab != null;
        }

        return false;
    }

    bool TryChooseGlobalResource(
        RoomResourceCategory allowedCategories,
        int roomIndex,
        System.Random random,
        out ResourceEntry selected)
    {
        selected = null;

        if (resources == null || resources.Length == 0)
            return false;

        float totalWeight = 0f;
        for (int i = 0; i < resources.Length; i++)
        {
            ResourceEntry entry = resources[i];
            if (entry == null || !entry.IsAvailableForRoom(roomIndex)) continue;
            if (!AllowsCategory(allowedCategories, entry.category)) continue;
            totalWeight += entry.weight;
        }

        if (totalWeight <= 0f)
            return false;

        double roll = random.NextDouble() * totalWeight;
        for (int i = 0; i < resources.Length; i++)
        {
            ResourceEntry entry = resources[i];
            if (entry == null || !entry.IsAvailableForRoom(roomIndex)) continue;
            if (!AllowsCategory(allowedCategories, entry.category)) continue;

            roll -= entry.weight;
            if (roll <= 0d)
            {
                selected = entry;
                return true;
            }
        }

        return false;
    }

    bool AllowsCategory(
        RoomResourceCategory allowedCategories,
        RoomResourceCategory category)
    {
        return category != RoomResourceCategory.None &&
            (allowedCategories & category) != RoomResourceCategory.None;
    }

    GameObject SpawnResource(
        ResourceSelection selection,
        Vector3 position,
        Quaternion rotation,
        GameObject room)
    {
        if (selection.prefab == null)
            return null;

        GameObject instance = Instantiate(selection.prefab, position, rotation);
        SnapResourceToGround(instance, room);

        NetworkManager networkManager = NetworkManager.Singleton;
        bool online = networkManager != null && networkManager.IsListening;

        if (!online)
            return instance;

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.Spawn(true);
            return instance;
        }

        if (requireNetworkObjectOnline)
        {
            if (logInvalidNetworkPrefabs)
            {
                string resourceName = string.IsNullOrWhiteSpace(selection.label)
                    ? selection.prefab.name
                    : selection.label;

                Debug.LogWarning(
                    $"Room resource '{resourceName}' needs a NetworkObject and NetworkManager registration for multiplayer spawning.");
            }

            Destroy(instance);
            return null;
        }

        return instance;
    }

    RoomDefinition GetRoomDefinition(GameObject room)
    {
        if (room == null)
            return null;

        RoomDefinition definition = room.GetComponent<RoomDefinition>();
        if (definition == null)
            definition = room.GetComponentInChildren<RoomDefinition>(true);

        return definition;
    }

    float GetEffectiveSpawnChanceMultiplier(RoomContentProfile contentProfile)
    {
        float contentMultiplier =
            contentProfile != null ? contentProfile.spawnChanceMultiplier : 1f;
        return spawnChanceMultiplier * Mathf.Max(0f, contentMultiplier);
    }

    void SnapResourceToGround(GameObject instance, GameObject room)
    {
        if (!snapSpawnedResourcesToGround || instance == null)
            return;

        Transform roomTransform = room != null ? room.transform : null;
        Vector3 up = roomTransform != null ? roomTransform.up : Vector3.up;
        Vector3 down = -up;

        Bounds bounds;
        bool hasBounds = TryGetResourceBounds(instance, out bounds);
        Vector3 boundsCenter = hasBounds
            ? bounds.center
            : instance.transform.position;
        Vector3 rayOrigin = boundsCenter + up * groundSnapRaycastHeight;
        float rayDistance = groundSnapRaycastHeight + groundSnapRaycastDistance;

        RaycastHit groundHit;
        if (!TryFindGroundBelow(
            instance,
            roomTransform,
            rayOrigin,
            down,
            rayDistance,
            out groundHit))
        {
            return;
        }

        float bottom = hasBounds
            ? GetMinProjection(bounds, up)
            : Vector3.Dot(instance.transform.position, up);
        float target = Vector3.Dot(groundHit.point, up) + groundContactOffset;
        instance.transform.position += up * (target - bottom);
    }

    bool TryFindGroundBelow(
        GameObject instance,
        Transform roomTransform,
        Vector3 rayOrigin,
        Vector3 down,
        float rayDistance,
        out RaycastHit groundHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            down,
            Mathf.Max(0.1f, rayDistance),
            groundSnapLayers,
            QueryTriggerInteraction.Ignore);

        bool foundGround = false;
        float closestDistance = float.PositiveInfinity;
        groundHit = new RaycastHit();
        Vector3 up = -down;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
                continue;

            if (hit.collider.transform.IsChildOf(instance.transform))
                continue;

            if (requireGroundInSameRoom &&
                roomTransform != null &&
                !hit.collider.transform.IsChildOf(roomTransform))
            {
                continue;
            }

            if (Vector3.Dot(hit.normal, up) < 0.5f)
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            groundHit = hit;
            foundGround = true;
        }

        return foundGround;
    }

    bool TryGetResourceBounds(GameObject instance, out Bounds bounds)
    {
        bounds = new Bounds(
            instance != null ? instance.transform.position : Vector3.zero,
            Vector3.zero);

        if (instance == null)
            return false;

        bool hasBounds = TryGetColliderBounds(instance, out bounds);
        if (hasBounds)
            return true;

        return TryGetRendererBounds(instance, out bounds);
    }

    bool TryGetColliderBounds(GameObject instance, out Bounds bounds)
    {
        bounds = new Bounds(instance.transform.position, Vector3.zero);
        Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
        bool hasBounds = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    bool TryGetRendererBounds(GameObject instance, out Bounds bounds)
    {
        bounds = new Bounds(instance.transform.position, Vector3.zero);
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    float GetMinProjection(Bounds bounds, Vector3 axis)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        float min = float.PositiveInfinity;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(
                        extents,
                        new Vector3(x, y, z));
                    min = Mathf.Min(min, Vector3.Dot(corner, axis));
                }
            }
        }

        return min;
    }

    void DespawnResource(GameObject instance)
    {
        if (instance == null) return;

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                networkObject.Despawn(true);

            return;
        }

        Destroy(instance);
    }

    bool CanSpawnAuthoritatively()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return true;

        return networkManager.IsServer;
    }

    int CreateRoomSeed(int runSeed, int roomIndex)
    {
        unchecked
        {
            int result = runSeed;
            result = result * 397 ^ seedOffset;
            result = result * 397 ^ roomIndex;
            return result;
        }
    }
}
