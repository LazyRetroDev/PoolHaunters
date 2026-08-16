using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class CursedSwimmingPoolMechanic : MonoBehaviour
{
    [Header("Pool")]
    [SerializeField] private SwimmingPoolObjective poolObjective;
    [SerializeField] private PoolCleanBoxItemConsumer cleanBox;
    [SerializeField] private string requiredItemName = "AguaBenta";

    [Header("Holy Water Spawn")]
    [SerializeField] private GameObject holyWaterPrefab;
    [SerializeField] private bool spawnHolyWaterAfterMapGeneration = true;
    [SerializeField] private bool avoidCurrentPoolRoom = true;
    [SerializeField] private bool avoidSubmarineAndFinalRooms = true;
    [SerializeField] private bool avoidPoolRooms = true;
    [SerializeField, Min(0)] private int spawnAttemptsPerRoom = 6;
    [SerializeField, Min(0f)] private float roomEdgePadding = 1.25f;
    [SerializeField, Min(0f)] private float floorOffset = 0.08f;
    [SerializeField] private LayerMask groundLayers = ~0;

    private bool blessed;
    private bool holyWaterSpawned;
    private Coroutine waitForMapRoutine;

    private void Awake()
    {
        AutoBindReferences();
        SetPoolLocked();
    }

    private void OnEnable()
    {
        AutoBindReferences();

        if (cleanBox != null)
            cleanBox.OnItemConsumed += HandleCleanBoxItemConsumed;

        RoomGenerator.OnGeneratedMapReady += HandleGeneratedMapReady;
        waitForMapRoutine = StartCoroutine(WaitForExistingGeneratedMap());
        SetPoolLocked();
    }

    private void OnDisable()
    {
        if (cleanBox != null)
            cleanBox.OnItemConsumed -= HandleCleanBoxItemConsumed;

        RoomGenerator.OnGeneratedMapReady -= HandleGeneratedMapReady;

        if (waitForMapRoutine != null)
        {
            StopCoroutine(waitForMapRoutine);
            waitForMapRoutine = null;
        }
    }

    private void HandleCleanBoxItemConsumed(Item item)
    {
        if (!IsRequiredItem(item))
            return;

        blessed = true;
        if (poolObjective != null)
            poolObjective.SetCleaningLocked(false);
    }

    private void HandleGeneratedMapReady(RoomGenerator generator)
    {
        TrySpawnHolyWater(generator);
    }

    private IEnumerator WaitForExistingGeneratedMap()
    {
        yield return null;

        RoomGenerator[] generators = FindObjectsByType<RoomGenerator>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < generators.Length; i++)
        {
            RoomGenerator generator = generators[i];
            if (generator != null && generator.IsGeneratedMapReady)
            {
                TrySpawnHolyWater(generator);
                break;
            }
        }

        waitForMapRoutine = null;
    }

    private void TrySpawnHolyWater(RoomGenerator generator)
    {
        if (!spawnHolyWaterAfterMapGeneration || holyWaterSpawned)
            return;
        if (generator == null || !CanSpawnAuthoritatively())
            return;

        RoomDefinition ownRoom = GetComponentInParent<RoomDefinition>();
        if (ownRoom != null && !generator.ContainsGeneratedRoom(ownRoom.gameObject))
            return;

        if (holyWaterPrefab == null)
        {
            Debug.LogWarning(
                $"{name} cannot spawn {requiredItemName} because no holyWaterPrefab is assigned.");
            return;
        }

        List<GameObject> eligibleRooms = GetEligibleRooms(generator, ownRoom);
        if (eligibleRooms.Count == 0)
        {
            Debug.LogWarning(
                $"{name} could not find an eligible room to spawn {requiredItemName}.");
            return;
        }

        System.Random random = new System.Random(CreateSpawnSeed(generator));
        while (eligibleRooms.Count > 0)
        {
            int roomListIndex = random.Next(eligibleRooms.Count);
            GameObject room = eligibleRooms[roomListIndex];
            eligibleRooms.RemoveAt(roomListIndex);

            RoomDefinition definition = room != null
                ? room.GetComponent<RoomDefinition>()
                : null;
            if (definition == null)
                continue;

            int attempts = Mathf.Max(1, spawnAttemptsPerRoom);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Vector3 position;
                Vector3 surfaceUp;
                Quaternion rotation;
                if (!TryGetSpawnPose(
                    definition,
                    random,
                    out position,
                    out surfaceUp,
                    out rotation))
                {
                    continue;
                }

                SpawnHolyWater(position, surfaceUp, rotation);
                return;
            }
        }

        Debug.LogWarning($"{name} failed to place {requiredItemName} after map generation.");
    }

    private List<GameObject> GetEligibleRooms(
        RoomGenerator generator,
        RoomDefinition ownRoom)
    {
        List<GameObject> rooms = generator.GetSpawnedRoomsSnapshot();
        List<GameObject> eligible = new List<GameObject>();

        for (int i = 0; i < rooms.Count; i++)
        {
            GameObject room = rooms[i];
            if (room == null)
                continue;
            if (avoidCurrentPoolRoom && ownRoom != null && room == ownRoom.gameObject)
                continue;

            RoomDefinition definition = room.GetComponent<RoomDefinition>();
            if (definition == null)
                continue;

            if (avoidSubmarineAndFinalRooms &&
                (definition.category == RoomCategory.SubmarineSpawn ||
                 definition.category == RoomCategory.Final))
            {
                continue;
            }

            if (avoidPoolRooms && definition.category == RoomCategory.Pool)
                continue;

            eligible.Add(room);
        }

        return eligible;
    }

    private bool TryGetSpawnPose(
        RoomDefinition definition,
        System.Random random,
        out Vector3 position,
        out Vector3 surfaceUp,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        surfaceUp = Vector3.up;
        rotation = Quaternion.identity;
        if (definition == null)
            return false;

        surfaceUp = definition.transform.up;
        Vector3 size = definition.size;
        float halfX = Mathf.Max(0f, size.x * 0.5f - roomEdgePadding);
        float halfZ = Mathf.Max(0f, size.z * 0.5f - roomEdgePadding);
        float localX = Mathf.Lerp(-halfX, halfX, (float)random.NextDouble());
        float localZ = Mathf.Lerp(-halfZ, halfZ, (float)random.NextDouble());
        Vector3 localTop = definition.boundsCenter +
            new Vector3(localX, size.y * 0.5f + 1f, localZ);
        Vector3 rayOrigin = definition.transform.TransformPoint(localTop);
        Vector3 down = -definition.transform.up;
        float rayDistance = Mathf.Max(3f, size.y + 3f);

        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            down,
            rayDistance,
            groundLayers,
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

            float height = Vector3.Dot(hit.point, definition.transform.up);
            if (height >= lowestHeight)
                continue;

            lowestHeight = height;
            floorHit = hit;
            foundFloor = true;
        }

        if (!foundFloor)
            return false;

        position = floorHit.point;
        float yaw = (float)random.NextDouble() * 360f;
        rotation = Quaternion.AngleAxis(yaw, definition.transform.up) *
            definition.transform.rotation;
        return true;
    }

    private void SpawnHolyWater(
        Vector3 surfacePoint,
        Vector3 surfaceUp,
        Quaternion rotation)
    {
        GameObject instance = Instantiate(
            holyWaterPrefab,
            surfacePoint + surfaceUp.normalized * floorOffset,
            rotation);
        SnapInstanceBaseToSurface(instance, surfacePoint, surfaceUp);
        holyWaterSpawned = true;

        NetworkManager networkManager = NetworkManager.Singleton;
        bool online = networkManager != null && networkManager.IsListening;
        if (!online)
            return;

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.Spawn(true);
            return;
        }

        Debug.LogWarning(
            $"{holyWaterPrefab.name} needs a NetworkObject to spawn from {name} in multiplayer.");
        Destroy(instance);
        holyWaterSpawned = false;
    }

    private void SnapInstanceBaseToSurface(
        GameObject instance,
        Vector3 surfacePoint,
        Vector3 surfaceUp)
    {
        if (instance == null)
            return;

        Vector3 up = surfaceUp.sqrMagnitude > 0.0001f
            ? surfaceUp.normalized
            : Vector3.up;

        Bounds bounds;
        if (!TryGetInstanceBounds(instance, out bounds))
        {
            instance.transform.position =
                surfacePoint + up * Mathf.Max(0f, floorOffset);
            return;
        }

        float bottom = GetMinProjection(bounds, up);
        float target = Vector3.Dot(surfacePoint, up) + Mathf.Max(0f, floorOffset);
        instance.transform.position += up * (target - bottom);
    }

    private bool TryGetInstanceBounds(GameObject instance, out Bounds bounds)
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

    private bool TryGetColliderBounds(GameObject instance, out Bounds bounds)
    {
        bounds = new Bounds(instance.transform.position, Vector3.zero);
        Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
        bool hasBounds = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider itemCollider = colliders[i];
            if (itemCollider == null ||
                !itemCollider.enabled ||
                itemCollider.isTrigger)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = itemCollider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(itemCollider.bounds);
            }
        }

        return hasBounds;
    }

    private bool TryGetRendererBounds(GameObject instance, out Bounds bounds)
    {
        bounds = new Bounds(instance.transform.position, Vector3.zero);
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer itemRenderer = renderers[i];
            if (itemRenderer == null || !itemRenderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = itemRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(itemRenderer.bounds);
            }
        }

        return hasBounds;
    }

    private float GetMinProjection(Bounds bounds, Vector3 axis)
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

    private bool IsRequiredItem(Item item)
    {
        if (item == null)
            return false;

        if (string.Equals(
            item.itemName,
            requiredItemName,
            System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            item.gameObject.name.Replace("(Clone)", string.Empty).Trim(),
            requiredItemName,
            System.StringComparison.OrdinalIgnoreCase);
    }

    private void SetPoolLocked()
    {
        if (poolObjective != null)
            poolObjective.SetCleaningLocked(!blessed);
    }

    private void AutoBindReferences()
    {
        if (poolObjective == null)
            poolObjective = GetComponent<SwimmingPoolObjective>();
        if (poolObjective == null)
            poolObjective = GetComponentInParent<SwimmingPoolObjective>();

        if (cleanBox == null)
            cleanBox = GetComponentInChildren<PoolCleanBoxItemConsumer>(true);
    }

    private bool CanSpawnAuthoritatively()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return true;

        return networkManager.IsServer;
    }

    private int CreateSpawnSeed(RoomGenerator generator)
    {
        unchecked
        {
            int result = generator != null ? generator.CurrentSeed : 0;
            result = result * 397 ^ (poolObjective != null ? poolObjective.SyncId : 0);
            result = result * 397 ^ requiredItemName.GetHashCode();
            return result;
        }
    }
}
