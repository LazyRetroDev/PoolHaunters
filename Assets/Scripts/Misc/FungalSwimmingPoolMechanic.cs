using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class FungalSwimmingPoolMechanic : MonoBehaviour
{
    [Header("Pool")]
    [SerializeField] private SwimmingPoolObjective poolObjective;

    [Header("Mushrooms")]
    [SerializeField] private FungalMushroomHazard mushroomPrefab;
    [SerializeField] private FungalMushroomHazard goodMushroomPrefab;
    [SerializeField] private Transform[] harmfulMushroomSpawnPoints = new Transform[0];
    [SerializeField] private Transform[] goodMushroomSpawnPoints = new Transform[0];
    [SerializeField] private bool autoFindSpawnPointGroups = true;
    [SerializeField] private string harmfulSpawnPointGroupName = "FungusSpawnPoints";
    [SerializeField] private string goodSpawnPointGroupName = "GoodFungusSpawnPoints";
    [SerializeField] private bool useCleanBoxAsGoodSpawnPoint = true;
    [SerializeField, Min(0)] private int mushroomsAroundPool = 6;
    [SerializeField, Min(0)] private int mushroomsAroundMap = 8;
    [SerializeField, Min(0)] private int maxHarmfulMushroomsAcrossLevel = 20;
    [SerializeField, Min(0)] private int goodMushroomsAroundPool = 2;
    [SerializeField] private bool spawnGoodMushroomsOutsidePoolRoom = true;
    [SerializeField, Range(0.05f, 1f)] private float goodMushroomCleanPortion = 0.35f;
    [SerializeField, Min(0.2f)] private float poolMushroomRadius = 4f;
    [SerializeField] private bool avoidPoolInteriorForPoolMushrooms = true;
    [SerializeField, Min(0f)] private float poolInteriorAvoidRadius = 2.5f;
    [SerializeField] private bool liftPoolMushroomsWhenFilled = true;
    [SerializeField] private Transform waterSurfaceReference;
    [SerializeField, Min(0f)] private float mushroomFloatAboveWater = 0.08f;
    [SerializeField, Min(1f)] private float mushroomFloatPoolRadiusMultiplier = 1.2f;
    [SerializeField, Min(0f)] private float spawnFloorOffset = 0.04f;
    [SerializeField, Range(0.45f, 1f)] private float minimumFloorNormalDot = 0.75f;
    [SerializeField, Min(0.1f)] private float spawnPointFloorSnapHeight = 2f;
    [SerializeField, Min(0.1f)] private float spawnPointFloorSnapDistance = 5f;
    [SerializeField, Min(1)] private int spawnAttemptsPerMushroom = 12;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private bool lockCleaningUntilMushroomsRemoved = true;

    private readonly HashSet<FungalMushroomHazard> activeMushrooms =
        new HashSet<FungalMushroomHazard>();

    private bool spawnedMapContent;
    private Coroutine waitForMapRoutine;

    public int ActiveMushroomCount
    {
        get
        {
            PruneMushrooms();
            return activeMushrooms.Count;
        }
    }
    public int ActiveHarmfulMushroomCount
    {
        get
        {
            int count = 0;
            FungalMushroomHazard[] mushrooms = GetActiveMushroomsSnapshot();
            for (int i = 0; i < mushrooms.Length; i++)
            {
                if (mushrooms[i] != null && !mushrooms[i].IsGoodFungus)
                    count++;
            }

            return count;
        }
    }

    private void Awake()
    {
        AutoBindReferences();
        RefreshPoolLock();
    }

    private void OnEnable()
    {
        AutoBindReferences();

        if (poolObjective != null)
        {
            poolObjective.OnPoolStateChanged -= HandlePoolStateChanged;
            poolObjective.OnPoolStateChanged += HandlePoolStateChanged;
        }

        RoomGenerator.OnGeneratedMapReady += HandleGeneratedMapReady;
        waitForMapRoutine = StartCoroutine(WaitForExistingGeneratedMap());
        RefreshPoolLock();
    }

    private void OnDisable()
    {
        if (poolObjective != null)
            poolObjective.OnPoolStateChanged -= HandlePoolStateChanged;

        RoomGenerator.OnGeneratedMapReady -= HandleGeneratedMapReady;

        if (waitForMapRoutine != null)
        {
            StopCoroutine(waitForMapRoutine);
            waitForMapRoutine = null;
        }
    }

    public void RegisterMushroom(FungalMushroomHazard mushroom)
    {
        if (mushroom == null)
            return;

        if (activeMushrooms.Add(mushroom))
            mushroom.BindPool(this);

        RefreshPoolLock();
    }

    public void NotifyMushroomRemoved(FungalMushroomHazard mushroom)
    {
        if (mushroom != null)
            activeMushrooms.Remove(mushroom);

        RefreshPoolLock();
    }

    public void RemoveFungusPortion(float portion, FungalMushroomHazard source)
    {
        portion = Mathf.Clamp01(portion);
        if (portion <= 0f)
            return;

        FungalMushroomHazard[] mushrooms = GetActiveMushroomsSnapshot();
        int removableCount = 0;
        for (int i = 0; i < mushrooms.Length; i++)
        {
            if (mushrooms[i] != null &&
                mushrooms[i] != source &&
                !mushrooms[i].IsGoodFungus)
            {
                removableCount++;
            }
        }

        int amountToRemove = Mathf.CeilToInt(removableCount * portion);
        for (int i = 0; i < mushrooms.Length && amountToRemove > 0; i++)
        {
            if (mushrooms[i] == null ||
                mushrooms[i] == source ||
                mushrooms[i].IsGoodFungus)
            {
                continue;
            }

            mushrooms[i].RemoveByHelpfulFungus();
            amountToRemove--;
        }

        RemoveAllGoodMushrooms();

        RefreshPoolLock();
    }

    private IEnumerator WaitForExistingGeneratedMap()
    {
        yield return null;

        RoomGenerator[] generators = FindObjectsByType<RoomGenerator>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < generators.Length; i++)
        {
            if (generators[i] != null && generators[i].IsGeneratedMapReady)
            {
                HandleGeneratedMapReady(generators[i]);
                break;
            }
        }

        waitForMapRoutine = null;
    }

    private void HandleGeneratedMapReady(RoomGenerator generator)
    {
        if (spawnedMapContent || generator == null || !CanSpawnAuthoritatively())
            return;

        RoomDefinition ownRoom = GetComponentInParent<RoomDefinition>();
        if (ownRoom != null && !generator.ContainsGeneratedRoom(ownRoom.gameObject))
            return;

        spawnedMapContent = true;
        int harmfulBudget = GetRemainingLevelHarmfulMushroomBudget();
        harmfulBudget -= SpawnPoolMushrooms(ownRoom, harmfulBudget);
        SpawnMapMushrooms(generator, ownRoom, harmfulBudget);
        SpawnGoodMushrooms(generator, ownRoom);
        if (poolObjective != null && poolObjective.IsFilled)
            FloatPoolMushroomsToWaterSurface();
        RefreshPoolLock();
    }

    private int SpawnPoolMushrooms(RoomDefinition ownRoom, int harmfulBudget)
    {
        if (mushroomPrefab == null || harmfulBudget <= 0)
            return 0;

        System.Random random = new System.Random(CreateSpawnSeed(17));
        Vector3 up = ownRoom != null ? ownRoom.transform.up : Vector3.up;
        int spawned = 0;

        if (mushroomPrefab != null && mushroomsAroundPool > 0)
        {
            int spawnedHarmfulFromPoints = SpawnFromPoints(
                harmfulMushroomSpawnPoints,
                false,
                mushroomPrefab,
                ownRoom,
                true,
                harmfulBudget);
            spawned += spawnedHarmfulFromPoints;
            int harmfulToRandomlySpawn =
                Mathf.Min(
                    Mathf.Max(0, mushroomsAroundPool - spawnedHarmfulFromPoints),
                    Mathf.Max(0, harmfulBudget - spawned));

            for (int i = 0; i < harmfulToRandomlySpawn; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float minimumDistance = avoidPoolInteriorForPoolMushrooms
                    ? Mathf.Max(poolMushroomRadius * 0.35f, poolInteriorAvoidRadius)
                    : poolMushroomRadius * 0.35f;
                float distance = Mathf.Lerp(
                    Mathf.Min(minimumDistance, poolMushroomRadius),
                    poolMushroomRadius,
                    (float)random.NextDouble());
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle) * distance,
                    0f,
                    Mathf.Sin(angle) * distance);
                Vector3 origin = transform.position + offset + up * 3f;

                Vector3 point;
                Vector3 surfaceUp;
                if (TryFindFloorInRoom(ownRoom, origin, -up, 8f, up, out point, out surfaceUp) &&
                    !IsInsideAvoidedPoolInterior(point))
                {
                    if (SpawnMushroom(point, surfaceUp, false))
                        spawned++;
                }
            }
        }

        return spawned;
    }

    private void SpawnGoodMushrooms(RoomGenerator generator, RoomDefinition ownRoom)
    {
        FungalMushroomHazard helperPrefab = goodMushroomPrefab != null
            ? goodMushroomPrefab
            : mushroomPrefab;
        if (helperPrefab == null || goodMushroomsAroundPool <= 0)
            return;

        if (spawnGoodMushroomsOutsidePoolRoom)
        {
            SpawnGoodMushroomsOutsideRoom(generator, ownRoom, helperPrefab);
            return;
        }

        System.Random random = new System.Random(CreateSpawnSeed(23));
        Vector3 up = ownRoom != null ? ownRoom.transform.up : Vector3.up;
        int spawnedGoodFromPoints = SpawnFromPoints(
            goodMushroomSpawnPoints,
            true,
            helperPrefab,
            ownRoom,
            false);
        int goodToRandomlySpawn =
            Mathf.Max(0, goodMushroomsAroundPool - spawnedGoodFromPoints);

        for (int i = 0; i < goodToRandomlySpawn; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Lerp(
                poolMushroomRadius * 0.2f,
                poolMushroomRadius * 0.8f,
                (float)random.NextDouble());
            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance);
            Vector3 origin = transform.position + offset + up * 3f;

            Vector3 point;
            Vector3 surfaceUp;
            if (TryFindFloorInRoom(ownRoom, origin, -up, 8f, up, out point, out surfaceUp))
            {
                SpawnMushroom(point, surfaceUp, true, helperPrefab);
            }
        }
    }

    private void SpawnGoodMushroomsOutsideRoom(
        RoomGenerator generator,
        RoomDefinition ownRoom,
        FungalMushroomHazard helperPrefab)
    {
        if (generator == null || helperPrefab == null || goodMushroomsAroundPool <= 0)
            return;

        List<GameObject> rooms = generator.GetSpawnedRoomsSnapshot();
        if (rooms.Count == 0)
            return;

        System.Random random = new System.Random(CreateSpawnSeed(23));
        int spawned = 0;
        int guard = goodMushroomsAroundPool * Mathf.Max(1, spawnAttemptsPerMushroom);

        while (spawned < goodMushroomsAroundPool && guard-- > 0)
        {
            GameObject room = rooms[random.Next(rooms.Count)];
            if (room == null)
                continue;
            if (ownRoom != null && room == ownRoom.gameObject)
                continue;

            RoomDefinition definition = room.GetComponent<RoomDefinition>();
            Vector3 point;
            Vector3 up;
            if (TryGetRandomRoomFloor(definition, random, out point, out up))
            {
                SpawnMushroom(point, up, true, helperPrefab);
                spawned++;
            }
        }
    }

    private void SpawnMapMushrooms(
        RoomGenerator generator,
        RoomDefinition ownRoom,
        int harmfulBudget)
    {
        if (mushroomPrefab == null || mushroomsAroundMap <= 0 || harmfulBudget <= 0)
            return;

        List<GameObject> rooms = generator.GetSpawnedRoomsSnapshot();
        if (rooms.Count == 0)
            return;

        System.Random random = new System.Random(CreateSpawnSeed(31));
        int spawned = 0;
        int targetCount = Mathf.Min(mushroomsAroundMap, harmfulBudget);
        int guard = targetCount * Mathf.Max(1, spawnAttemptsPerMushroom);

        while (spawned < targetCount && guard-- > 0)
        {
            GameObject room = rooms[random.Next(rooms.Count)];
            if (room == null)
                continue;
            if (ownRoom != null && room == ownRoom.gameObject)
                continue;

            RoomDefinition definition = room.GetComponent<RoomDefinition>();
            Vector3 point;
            Vector3 up;
            if (TryGetRandomRoomFloor(definition, random, out point, out up))
            {
                if (SpawnMushroom(point, up, false))
                    spawned++;
            }
        }
    }

    private bool SpawnMushroom(
        Vector3 surfacePoint,
        Vector3 surfaceUp,
        bool goodFungus,
        FungalMushroomHazard prefabOverride = null)
    {
        FungalMushroomHazard prefab = prefabOverride != null
            ? prefabOverride
            : mushroomPrefab;
        if (prefab == null)
            return false;

        FungalMushroomHazard mushroom = Instantiate(
            prefab,
            surfacePoint + surfaceUp.normalized * spawnFloorOffset,
            GetSurfaceRotation(surfaceUp));

        mushroom.SetGoodFungus(goodFungus);
        mushroom.SetGoodFungusCleanPortion(goodMushroomCleanPortion);
        mushroom.BindPool(this);
        RegisterMushroom(mushroom);

        NetworkObject networkObject = mushroom.GetComponent<NetworkObject>();
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null &&
            networkManager.IsListening &&
            networkManager.IsServer &&
            networkObject != null &&
            !networkObject.IsSpawned)
        {
            networkObject.Spawn(true);
        }

        return true;
    }

    private bool TryGetRandomRoomFloor(
        RoomDefinition definition,
        System.Random random,
        out Vector3 point,
        out Vector3 up)
    {
        point = Vector3.zero;
        up = Vector3.up;

        if (definition == null)
            return false;

        up = definition.transform.up;
        Vector3 size = definition.size;
        float localX = Mathf.Lerp(-size.x * 0.35f, size.x * 0.35f, (float)random.NextDouble());
        float localZ = Mathf.Lerp(-size.z * 0.35f, size.z * 0.35f, (float)random.NextDouble());
        Vector3 origin = definition.transform.TransformPoint(
            definition.boundsCenter + new Vector3(localX, size.y * 0.5f + 1f, localZ));

        return TryFindFloorInRoom(
            definition,
            origin,
            -up,
            Mathf.Max(4f, size.y + 4f),
            up,
            out point,
            out up);
    }

    private bool TryFindFloorInRoom(
        RoomDefinition definition,
        Vector3 origin,
        Vector3 direction,
        float distance,
        Vector3 expectedUp,
        out Vector3 point,
        out Vector3 surfaceUp)
    {
        if (definition == null)
        {
            return TryFindFloor(
                origin,
                direction,
                distance,
                expectedUp,
                out point,
                out surfaceUp);
        }

        point = Vector3.zero;
        surfaceUp = definition.transform.up;
        Vector3 floorUp = surfaceUp.sqrMagnitude > 0.0001f
            ? surfaceUp.normalized
            : Vector3.up;
        Vector3 halfSize = definition.size * 0.5f;
        float lowestLocalY = float.PositiveInfinity;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            distance,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null)
                continue;
            if (!IsFloorNormal(hits[i].normal, floorUp))
                continue;

            Vector3 localPoint = definition.transform.InverseTransformPoint(hits[i].point);
            if (!IsInsideRoomFootprint(localPoint, definition.boundsCenter, halfSize))
                continue;

            float localY = localPoint.y;
            if (localY >= lowestLocalY)
                continue;

            lowestLocalY = localY;
            point = hits[i].point;
            surfaceUp = hits[i].normal.normalized;
        }

        return lowestLocalY < float.PositiveInfinity;
    }

    private bool TryFindFloor(
        Vector3 origin,
        Vector3 direction,
        float distance,
        Vector3 expectedUp,
        out Vector3 point,
        out Vector3 surfaceUp)
    {
        point = Vector3.zero;
        surfaceUp = expectedUp.sqrMagnitude > 0.0001f
            ? expectedUp.normalized
            : Vector3.up;
        Vector3 floorUp = surfaceUp;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            distance,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null)
                continue;
            if (!IsFloorNormal(hits[i].normal, floorUp))
                continue;
            if (hits[i].distance >= bestDistance)
                continue;

            bestDistance = hits[i].distance;
            point = hits[i].point;
            surfaceUp = hits[i].normal.normalized;
        }

        return bestDistance < float.PositiveInfinity;
    }

    private bool IsInsideRoomFootprint(
        Vector3 localPoint,
        Vector3 boundsCenter,
        Vector3 halfSize)
    {
        const float Padding = 0.15f;
        return localPoint.x >= boundsCenter.x - halfSize.x - Padding &&
            localPoint.x <= boundsCenter.x + halfSize.x + Padding &&
            localPoint.z >= boundsCenter.z - halfSize.z - Padding &&
            localPoint.z <= boundsCenter.z + halfSize.z + Padding;
    }

    private bool TrySnapSpawnPointToFloor(
        Transform spawnPoint,
        RoomDefinition fallbackRoom,
        out Vector3 point,
        out Vector3 surfaceUp)
    {
        point = Vector3.zero;
        surfaceUp = Vector3.up;

        if (spawnPoint == null)
            return false;

        RoomDefinition room = spawnPoint.GetComponentInParent<RoomDefinition>();
        if (room == null)
            room = fallbackRoom;

        Vector3 expectedUp = room != null ? room.transform.up : Vector3.up;
        Vector3 origin = spawnPoint.position + expectedUp.normalized * spawnPointFloorSnapHeight;
        return TryFindFloorInRoom(
            room,
            origin,
            -expectedUp,
            spawnPointFloorSnapHeight + spawnPointFloorSnapDistance,
            expectedUp,
            out point,
            out surfaceUp);
    }

    private bool IsFloorNormal(Vector3 surfaceNormal, Vector3 expectedUp)
    {
        if (surfaceNormal.sqrMagnitude <= 0.0001f)
            return false;

        Vector3 up = expectedUp.sqrMagnitude > 0.0001f
            ? expectedUp.normalized
            : Vector3.up;
        return Vector3.Dot(surfaceNormal.normalized, up) >= minimumFloorNormalDot;
    }

    private void HandlePoolStateChanged(SwimmingPoolObjective pool)
    {
        if (pool != poolObjective || !liftPoolMushroomsWhenFilled || !poolObjective.IsFilled)
            return;

        FloatPoolMushroomsToWaterSurface();
    }

    private void FloatPoolMushroomsToWaterSurface()
    {
        if (!TryGetWaterSurfaceHeight(out float waterSurfaceHeight, out Vector3 up))
            return;

        FungalMushroomHazard[] mushrooms = GetActiveMushroomsSnapshot();
        for (int i = 0; i < mushrooms.Length; i++)
        {
            FungalMushroomHazard mushroom = mushrooms[i];
            if (mushroom == null || !IsInsideMushroomFloatArea(mushroom.transform.position))
                continue;

            Vector3 position = mushroom.transform.position;
            float currentHeight = Vector3.Dot(position, up);
            float targetHeight = waterSurfaceHeight + mushroomFloatAboveWater;
            if (currentHeight >= targetHeight)
                continue;

            mushroom.transform.position = position + up * (targetHeight - currentHeight);
        }
    }

    private bool TryGetWaterSurfaceHeight(out float surfaceHeight, out Vector3 up)
    {
        up = transform.up.sqrMagnitude > 0.0001f
            ? transform.up.normalized
            : Vector3.up;
        surfaceHeight = 0f;

        Transform surface = waterSurfaceReference != null
            ? waterSurfaceReference
            : FindWaterSurfaceReference();
        if (surface == null)
            return false;

        Renderer[] renderers = surface.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            surfaceHeight = Vector3.Dot(surface.position, up);
            return true;
        }

        float highest = float.NegativeInfinity;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            Bounds bounds = renderers[i].bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(
                            extents,
                            new Vector3(x, y, z));
                        highest = Mathf.Max(highest, Vector3.Dot(corner, up));
                    }
                }
            }
        }

        if (highest <= float.NegativeInfinity)
            return false;

        surfaceHeight = highest;
        return true;
    }

    private Transform FindWaterSurfaceReference()
    {
        string[] names =
        {
            "WaterVisual",
            "ContaminatedWater",
            "DirtyWater",
            "CleanWater",
            "Water"
        };

        for (int i = 0; i < names.Length; i++)
        {
            Transform found = FindChildRecursive(transform, names[i]);
            if (found != null)
            {
                waterSurfaceReference = found;
                return found;
            }
        }

        return null;
    }

    private bool IsInsideMushroomFloatArea(Vector3 point)
    {
        float radius = Mathf.Max(0f, poolInteriorAvoidRadius) *
            Mathf.Max(1f, mushroomFloatPoolRadiusMultiplier);
        if (radius <= 0f)
            radius = Mathf.Max(0.5f, poolMushroomRadius * 0.65f);

        Vector3 up = transform.up.sqrMagnitude > 0.0001f
            ? transform.up.normalized
            : Vector3.up;
        Vector3 offset = Vector3.ProjectOnPlane(point - transform.position, up);
        return offset.magnitude <= radius;
    }

    private bool IsInsideAvoidedPoolInterior(Vector3 point)
    {
        if (!avoidPoolInteriorForPoolMushrooms || poolInteriorAvoidRadius <= 0f)
            return false;

        Vector3 up = transform.up.sqrMagnitude > 0.0001f
            ? transform.up.normalized
            : Vector3.up;
        Vector3 offset = Vector3.ProjectOnPlane(point - transform.position, up);
        return offset.magnitude < poolInteriorAvoidRadius;
    }

    private void RefreshPoolLock()
    {
        if (poolObjective == null)
            return;

        bool hasMushrooms = lockCleaningUntilMushroomsRemoved && HasActiveMushrooms();
        poolObjective.SetCleaningLocked(hasMushrooms);
    }

    private bool HasActiveMushrooms()
    {
        PruneMushrooms();
        foreach (FungalMushroomHazard mushroom in activeMushrooms)
        {
            if (mushroom != null && !mushroom.IsGoodFungus)
                return true;
        }

        return false;
    }

    private void AutoBindReferences()
    {
        if (poolObjective == null)
            poolObjective = GetComponent<SwimmingPoolObjective>();
        if (poolObjective == null)
            poolObjective = GetComponentInParent<SwimmingPoolObjective>();
        if (waterSurfaceReference == null)
            waterSurfaceReference = FindWaterSurfaceReference();

        if (autoFindSpawnPointGroups)
        {
            if (harmfulMushroomSpawnPoints == null ||
                harmfulMushroomSpawnPoints.Length == 0)
            {
                harmfulMushroomSpawnPoints =
                    FindSpawnPointsInGroup(harmfulSpawnPointGroupName);
            }

            if (goodMushroomSpawnPoints == null ||
                goodMushroomSpawnPoints.Length == 0)
            {
                goodMushroomSpawnPoints =
                    FindSpawnPointsInGroup(goodSpawnPointGroupName);
            }
        }

        if (useCleanBoxAsGoodSpawnPoint &&
            (goodMushroomSpawnPoints == null ||
             goodMushroomSpawnPoints.Length == 0))
        {
            Transform cleanBox = FindChildRecursive(transform, "CleanBox");
            if (cleanBox != null)
                goodMushroomSpawnPoints = new[] { cleanBox };
        }
    }

    private bool CanSpawnAuthoritatively()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null || !networkManager.IsListening || networkManager.IsServer;
    }

    private int CreateSpawnSeed(int salt)
    {
        unchecked
        {
            int hash = poolObjective != null ? poolObjective.SyncId : transform.position.GetHashCode();
            hash = hash * 397 ^ salt;
            return hash;
        }
    }

    private Quaternion GetSurfaceRotation(Vector3 surfaceUp)
    {
        Vector3 up = surfaceUp.sqrMagnitude > 0.0001f
            ? surfaceUp.normalized
            : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up);
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.ProjectOnPlane(Vector3.right, up);

        return Quaternion.LookRotation(forward.normalized, up);
    }

    private void PruneMushrooms()
    {
        activeMushrooms.RemoveWhere(mushroom => mushroom == null);
    }

    private FungalMushroomHazard[] GetActiveMushroomsSnapshot()
    {
        PruneMushrooms();
        FungalMushroomHazard[] mushrooms =
            new FungalMushroomHazard[activeMushrooms.Count];
        activeMushrooms.CopyTo(mushrooms);
        return mushrooms;
    }

    private int GetRemainingLevelHarmfulMushroomBudget()
    {
        int cap = Mathf.Max(0, maxHarmfulMushroomsAcrossLevel);
        if (cap <= 0)
            return 0;

        FungalSwimmingPoolMechanic[] pools =
            FindObjectsByType<FungalSwimmingPoolMechanic>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        int activeHarmfulMushrooms = 0;
        for (int i = 0; i < pools.Length; i++)
        {
            if (pools[i] != null)
                activeHarmfulMushrooms += pools[i].ActiveHarmfulMushroomCount;
        }

        return Mathf.Max(0, cap - activeHarmfulMushrooms);
    }

    private void RemoveAllGoodMushrooms()
    {
        FungalMushroomHazard[] mushrooms = GetActiveMushroomsSnapshot();
        for (int i = 0; i < mushrooms.Length; i++)
        {
            if (mushrooms[i] != null && mushrooms[i].IsGoodFungus)
                mushrooms[i].RemoveByHelpfulFungus();
        }
    }

    private int SpawnFromPoints(
        Transform[] spawnPoints,
        bool goodFungus,
        FungalMushroomHazard prefab,
        RoomDefinition fallbackRoom,
        bool avoidPoolInterior,
        int maxSpawnCount = int.MaxValue)
    {
        if (spawnPoints == null || prefab == null)
            return 0;

        int spawned = 0;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawned >= maxSpawnCount)
                break;

            Transform point = spawnPoints[i];
            if (point == null)
                continue;

            Vector3 surfacePoint;
            Vector3 surfaceUp;
            if (!TrySnapSpawnPointToFloor(point, fallbackRoom, out surfacePoint, out surfaceUp))
                continue;
            if (avoidPoolInterior && IsInsideAvoidedPoolInterior(surfacePoint))
                continue;

            if (SpawnMushroom(surfacePoint, surfaceUp, goodFungus, prefab))
                spawned++;
        }

        return spawned;
    }

    private Transform[] FindSpawnPointsInGroup(string groupName)
    {
        Transform group = FindChildRecursive(transform, groupName);
        if (group == null)
            return new Transform[0];

        Transform[] points = new Transform[group.childCount];
        for (int i = 0; i < group.childCount; i++)
            points[i] = group.GetChild(i);

        return points;
    }

    private Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }
}
