using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class RoomEnemySpawner : MonoBehaviour
{
    struct EnemySelection
    {
        public string label;
        public GameObject prefab;
        public RoomEnemyCategory category;

        public EnemySelection(RoomContentProfile.EnemyEntry entry)
        {
            label = entry != null ? entry.label : string.Empty;
            prefab = entry != null ? entry.prefab : null;
            category = entry != null ? entry.category : RoomEnemyCategory.None;
        }
    }

    [Header("Randomization")]
    public int seedOffset = 9361;

    [Range(0f, 2f)]
    public float spawnChanceMultiplier = 1f;

    [Header("Global Phase Selection")]
    [Tooltip("Selects one exact enemy plan for the whole run instead of rolling independently at every room point.")]
    public bool usePhaseBasedRunSelection = true;

    [Tooltip("Optional global roster. When empty, the first generated room content profile with enemies is used.")]
    public RoomContentProfile runEnemyProfile;

    [Min(1)]
    [Tooltip("Attempts used to find a separated NavMesh position when all authored enemy points are occupied.")]
    public int fallbackPlacementAttemptsPerEnemy = 40;

    [Min(0f)]
    public float minimumEnemySeparation = 2.5f;

    [Min(0.1f)]
    public float fallbackNavMeshSampleRadius = 6f;

    public bool logRunEnemyPlan = true;

    [Header("Dynamic Encounter Director")]
    [Tooltip("Controls when planned monsters become physically active. It never changes the phase plan or its total count.")]
    public MonsterEncounterDirectorSettings encounterDirectorSettings =
        new MonsterEncounterDirectorSettings();

    [SerializeField] private MonsterEncounterDirector encounterDirector;

    [Header("NavMesh Placement")]
    public bool requireNavMeshPosition = true;

    [Min(0.1f)]
    public float navMeshSampleRadius = 2f;

    public int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Multiplayer")]
    [Tooltip("During a network session, enemy prefabs must have a NetworkObject and be registered with NetworkManager.")]
    public bool requireNetworkObjectOnline = true;

    [Tooltip("Warn when a selected online prefab cannot be network-spawned.")]
    public bool logInvalidNetworkPrefabs = true;

    private readonly Dictionary<GameObject, List<GameObject>> enemiesByRoom =
        new Dictionary<GameObject, List<GameObject>>();

    private readonly List<GameObject> runEnemies = new List<GameObject>();

    private readonly Collider[] spawnOverlapBuffer = new Collider[32];

    [SerializeField] private bool runEnemiesSpawned;
    [SerializeField] private int lastPlannedPhase = 1;
    [SerializeField] private int lastPlannedEnemyCount;
    [SerializeField] private string lastPlannedEnemies = string.Empty;

    public bool UsesPhaseBasedRunSelection
    {
        get { return usePhaseBasedRunSelection; }
    }

    void Awake()
    {
        if (encounterDirectorSettings == null)
            encounterDirectorSettings = new MonsterEncounterDirectorSettings();

        EnsureEncounterDirector();
    }

    public void SpawnEnemiesForRoom(GameObject room, int roomIndex, int runSeed)
    {
        if (room == null || !CanSpawnAuthoritatively()) return;

        if (usePhaseBasedRunSelection)
            return;

        RoomDefinition definition = GetRoomDefinition(room);
        RoomContentProfile contentProfile =
            definition != null ? definition.contentProfile : null;

        if (contentProfile == null || !contentProfile.HasEnemyTable)
            return;

        RoomEnemySpawnPoint[] spawnPoints =
            room.GetComponentsInChildren<RoomEnemySpawnPoint>(true);
        if (spawnPoints == null || spawnPoints.Length == 0)
            return;

        System.Random random = new System.Random(
            CreateRoomSeed(runSeed, roomIndex));
        List<GameObject> spawnedEnemies = new List<GameObject>();
        float effectiveSpawnChanceMultiplier =
            GetEffectiveSpawnChanceMultiplier(contentProfile);

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            RoomEnemySpawnPoint point = spawnPoints[i];
            if (point == null) continue;

            float chance = Mathf.Clamp01(
                point.spawnChance * effectiveSpawnChanceMultiplier);
            if (random.NextDouble() > chance) continue;

            EnemySelection selected;
            if (!TryChooseEnemy(
                contentProfile,
                point.allowedCategories,
                roomIndex,
                random,
                out selected))
            {
                continue;
            }

            point.GetSpawnPose(out Vector3 position, out Quaternion rotation);
            if (!TryResolveNavMeshPose(position, rotation, out position, out rotation))
                continue;

            GameObject instance = SpawnEnemy(selected, position, rotation);
            if (instance != null)
                spawnedEnemies.Add(instance);
        }

        if (spawnedEnemies.Count > 0)
            enemiesByRoom[room] = spawnedEnemies;
    }

    public void SpawnEnemiesForGeneratedMap(
        IReadOnlyList<GameObject> rooms,
        int runSeed)
    {
        if (!usePhaseBasedRunSelection || runEnemiesSpawned)
            return;
        if (!CanSpawnAuthoritatively())
            return;

        RoomContentProfile profile = ResolveRunEnemyProfile(rooms);
        if (profile == null || !profile.HasEnemyTable)
            return;

        int phase = RegionRunState.HasSelectedRegion
            ? Mathf.Max(1, RegionRunState.PhaseNumber)
            : 1;
        int planSeed = CreateRunPlanSeed(runSeed, phase);
        List<int> selectedEntryIndices = new List<int>();
        List<RunEnemyCandidate> candidates = BuildRunCandidates(profile);

        if (!RunEnemySelection.TryBuildPlan(
            candidates,
            phase,
            planSeed,
            selectedEntryIndices))
        {
            Debug.LogError(
                $"RoomEnemySpawner could not create a valid enemy plan for phase {phase}. Check enemy difficulties and support flags.");
            return;
        }

        List<string> plannedNames = new List<string>();
        for (int i = 0; i < selectedEntryIndices.Count; i++)
        {
            int entryIndex = selectedEntryIndices[i];
            if (entryIndex < 0 || entryIndex >= profile.enemies.Length)
                continue;

            RoomContentProfile.EnemyEntry entry = profile.enemies[entryIndex];
            if (entry == null || entry.prefab == null)
                continue;

            plannedNames.Add(string.IsNullOrWhiteSpace(entry.label)
                ? entry.prefab.name
                : entry.label);
        }

        lastPlannedPhase = phase;
        lastPlannedEnemyCount = selectedEntryIndices.Count;
        lastPlannedEnemies = string.Join(", ", plannedNames);

        if (plannedNames.Count != selectedEntryIndices.Count)
        {
            Debug.LogError(
                $"RoomEnemySpawner planned {selectedEntryIndices.Count} enemies for phase {phase}, but {plannedNames.Count} valid catalog entries were found.");
            return;
        }

        EnsureEncounterDirector();
        if (encounterDirectorSettings != null &&
            encounterDirectorSettings.useDynamicEncounters)
        {
            if (encounterDirector == null ||
                !encounterDirector.InitializePlan(
                    profile,
                    selectedEntryIndices,
                    rooms,
                    planSeed,
                    phase))
            {
                Debug.LogError(
                    $"RoomEnemySpawner could not initialize the encounter director for phase {phase}.");
                return;
            }

            runEnemiesSpawned = true;
        }
        else
        {
            runEnemiesSpawned = SpawnCompletePlanImmediately(
                profile,
                selectedEntryIndices,
                rooms,
                planSeed,
                phase);
        }

        if (runEnemiesSpawned && logRunEnemyPlan)
        {
            Debug.Log(
                $"Phase {phase} enemy plan ({selectedEntryIndices.Count}): {lastPlannedEnemies}");
        }
    }

    bool SpawnCompletePlanImmediately(
        RoomContentProfile profile,
        IReadOnlyList<int> selectedEntryIndices,
        IReadOnlyList<GameObject> rooms,
        int planSeed,
        int phase)
    {
        List<RoomEnemySpawnPoint> availablePoints = CollectSpawnPoints(rooms);
        System.Random placementRandom = new System.Random(
            unchecked(planSeed ^ 0x2F6E2B1));
        Shuffle(availablePoints, placementRandom);

        List<Vector3> occupiedPositions = new List<Vector3>();
        List<GameObject> spawnedThisPlan = new List<GameObject>();
        int spawnedCount = 0;
        for (int i = 0; i < selectedEntryIndices.Count; i++)
        {
            RoomContentProfile.EnemyEntry entry =
                profile.enemies[selectedEntryIndices[i]];
            if (!TryFindRunSpawnPose(
                entry,
                rooms,
                availablePoints,
                occupiedPositions,
                placementRandom,
                out Vector3 position,
                out Quaternion rotation))
            {
                Debug.LogWarning(
                    $"RoomEnemySpawner found no NavMesh position for planned enemy '{entry.label}' in phase {phase}.");
                continue;
            }

            GameObject instance = SpawnEnemy(
                new EnemySelection(entry),
                position,
                rotation);
            if (instance == null)
                continue;

            runEnemies.Add(instance);
            spawnedThisPlan.Add(instance);
            occupiedPositions.Add(position);
            spawnedCount++;
        }

        if (spawnedCount == selectedEntryIndices.Count)
            return true;

        Debug.LogError(
            $"RoomEnemySpawner planned {selectedEntryIndices.Count} enemies for phase {phase}, but spawned {spawnedCount}.");

        for (int i = spawnedThisPlan.Count - 1; i >= 0; i--)
            DespawnDirectedEnemy(spawnedThisPlan[i]);

        return false;
    }

    public void DespawnRunEnemies()
    {
        if (encounterDirector != null && encounterDirector.HasPlan)
            encounterDirector.ResetRun();

        if (CanSpawnAuthoritatively())
        {
            for (int i = runEnemies.Count - 1; i >= 0; i--)
                DespawnEnemy(runEnemies[i]);
        }

        runEnemies.Clear();
        runEnemiesSpawned = false;
        lastPlannedEnemyCount = 0;
        lastPlannedEnemies = string.Empty;
    }

    public void DespawnEnemiesForRoom(GameObject room)
    {
        if (room == null || !enemiesByRoom.TryGetValue(room, out List<GameObject> spawned))
            return;

        if (CanSpawnAuthoritatively())
        {
            for (int i = 0; i < spawned.Count; i++)
                DespawnEnemy(spawned[i]);
        }

        enemiesByRoom.Remove(room);
    }

    RoomContentProfile ResolveRunEnemyProfile(
        IReadOnlyList<GameObject> rooms)
    {
        if (runEnemyProfile != null && runEnemyProfile.HasEnemyTable)
            return runEnemyProfile;

        if (rooms == null)
            return null;

        for (int i = 0; i < rooms.Count; i++)
        {
            RoomDefinition definition = GetRoomDefinition(rooms[i]);
            RoomContentProfile profile =
                definition != null ? definition.contentProfile : null;
            if (profile != null && profile.HasEnemyTable)
                return profile;
        }

        return null;
    }

    List<RunEnemyCandidate> BuildRunCandidates(
        RoomContentProfile profile)
    {
        List<RunEnemyCandidate> candidates =
            new List<RunEnemyCandidate>();

        if (profile == null || profile.enemies == null)
            return candidates;

        for (int i = 0; i < profile.enemies.Length; i++)
        {
            RoomContentProfile.EnemyEntry entry = profile.enemies[i];
            if (entry == null || entry.prefab == null || entry.weight <= 0f)
                continue;

            candidates.Add(new RunEnemyCandidate(
                i,
                entry.difficulty,
                entry.requiresCompanion,
                entry.weight));
        }

        return candidates;
    }

    List<RoomEnemySpawnPoint> CollectSpawnPoints(
        IReadOnlyList<GameObject> rooms)
    {
        List<RoomEnemySpawnPoint> points =
            new List<RoomEnemySpawnPoint>();

        if (rooms == null)
            return points;

        for (int i = 0; i < rooms.Count; i++)
        {
            GameObject room = rooms[i];
            if (room == null)
                continue;

            RoomEnemySpawnPoint[] roomPoints =
                room.GetComponentsInChildren<RoomEnemySpawnPoint>(true);
            for (int pointIndex = 0; pointIndex < roomPoints.Length; pointIndex++)
            {
                RoomEnemySpawnPoint point = roomPoints[pointIndex];
                if (point != null && point.gameObject.activeInHierarchy)
                    points.Add(point);
            }
        }

        return points;
    }

    bool TryFindRunSpawnPose(
        RoomContentProfile.EnemyEntry entry,
        IReadOnlyList<GameObject> rooms,
        List<RoomEnemySpawnPoint> availablePoints,
        List<Vector3> occupiedPositions,
        System.Random random,
        out Vector3 position,
        out Quaternion rotation)
    {
        while (availablePoints.Count > 0)
        {
            int lastIndex = availablePoints.Count - 1;
            RoomEnemySpawnPoint point = availablePoints[lastIndex];
            availablePoints.RemoveAt(lastIndex);

            if (point == null ||
                !AllowsCategory(point.allowedCategories, entry.category))
            {
                continue;
            }

            point.GetSpawnPose(out position, out rotation);
            if (!TryResolveNavMeshPose(position, rotation, out position, out rotation))
                continue;
            if (!IsFarEnoughFromRunEnemies(position, occupiedPositions))
                continue;

            return true;
        }

        return TryFindFallbackPose(
            rooms,
            occupiedPositions,
            random,
            out position,
            out rotation);
    }

    bool TryFindFallbackPose(
        IReadOnlyList<GameObject> rooms,
        List<Vector3> occupiedPositions,
        System.Random random,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (rooms == null || rooms.Count == 0)
            return false;

        int attempts = Mathf.Max(1, fallbackPlacementAttemptsPerEnemy);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            GameObject room = rooms[random.Next(0, rooms.Count)];
            if (room == null)
                continue;

            RoomDefinition definition = GetRoomDefinition(room);
            Vector3 localCenter = definition != null
                ? definition.boundsCenter
                : Vector3.zero;
            Vector3 size = definition != null
                ? definition.size
                : new Vector3(20f, 4f, 20f);

            float halfWidth = Mathf.Max(1f, Mathf.Abs(size.x) * 0.45f);
            float halfDepth = Mathf.Max(1f, Mathf.Abs(size.z) * 0.45f);
            Vector3 localCandidate = localCenter + new Vector3(
                NextFloat(random, -halfWidth, halfWidth),
                0f,
                NextFloat(random, -halfDepth, halfDepth));
            Vector3 worldCandidate = room.transform.TransformPoint(localCandidate);
            Quaternion requestedRotation = Quaternion.Euler(
                0f,
                NextFloat(random, 0f, 360f),
                0f);

            if (requireNavMeshPosition)
            {
                if (!NavMesh.SamplePosition(
                    worldCandidate,
                    out NavMeshHit hit,
                    fallbackNavMeshSampleRadius,
                    navMeshAreaMask))
                {
                    continue;
                }

                worldCandidate = hit.position;
            }

            if (!IsFarEnoughFromRunEnemies(worldCandidate, occupiedPositions))
                continue;

            position = worldCandidate;
            rotation = requestedRotation;
            return true;
        }

        return false;
    }

    bool IsFarEnoughFromRunEnemies(
        Vector3 position,
        List<Vector3> occupiedPositions)
    {
        float minimumDistance = Mathf.Max(0f, minimumEnemySeparation);
        if (minimumDistance <= 0f)
            return true;

        float minimumDistanceSquared = minimumDistance * minimumDistance;
        for (int i = 0; i < occupiedPositions.Count; i++)
        {
            if ((position - occupiedPositions[i]).sqrMagnitude <
                minimumDistanceSquared)
            {
                return false;
            }
        }

        return true;
    }

    bool AllowsCategory(
        RoomEnemyCategory allowedCategories,
        RoomEnemyCategory category)
    {
        return category != RoomEnemyCategory.None &&
            (allowedCategories & category) != RoomEnemyCategory.None;
    }

    void Shuffle<T>(List<T> values, System.Random random)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(0, i + 1);
            T temporary = values[i];
            values[i] = values[swapIndex];
            values[swapIndex] = temporary;
        }
    }

    float NextFloat(System.Random random, float minimum, float maximum)
    {
        return minimum +
            (float)random.NextDouble() * (maximum - minimum);
    }

    void EnsureEncounterDirector()
    {
        if (encounterDirector == null)
            encounterDirector = GetComponent<MonsterEncounterDirector>();
        if (encounterDirector == null)
            encounterDirector = gameObject.AddComponent<MonsterEncounterDirector>();

        encounterDirector.Configure(this);
    }

    internal bool CanRunEncounterDirector()
    {
        return CanSpawnAuthoritatively();
    }

    internal bool TrySpawnDirectedEnemy(
        RoomContentProfile.EnemyEntry entry,
        IReadOnlyList<GameObject> rooms,
        PlayerStatus[] players,
        PlayerStatus preferredPlayer,
        System.Random random,
        out GameObject instance)
    {
        instance = null;
        if (entry == null || entry.prefab == null ||
            players == null || players.Length == 0 ||
            !CanSpawnAuthoritatively())
        {
            return false;
        }

        List<RoomEnemySpawnPoint> availablePoints = CollectSpawnPoints(rooms);
        Shuffle(availablePoints, random);
        List<Vector3> occupiedPositions = CollectActiveRunEnemyPositions();

        while (availablePoints.Count > 0)
        {
            int lastIndex = availablePoints.Count - 1;
            RoomEnemySpawnPoint point = availablePoints[lastIndex];
            availablePoints.RemoveAt(lastIndex);
            if (point == null ||
                !AllowsCategory(point.allowedCategories, entry.category))
            {
                continue;
            }

            point.GetSpawnPose(out Vector3 position, out Quaternion rotation);
            if (!TryResolveNavMeshPose(position, rotation, out position, out rotation))
                continue;
            if (!IsDirectedSpawnPoseSafe(
                position,
                occupiedPositions,
                players,
                preferredPlayer))
            {
                continue;
            }

            instance = SpawnEnemy(new EnemySelection(entry), position, rotation);
            if (instance != null)
                runEnemies.Add(instance);

            return instance != null;
        }

        if (!TryFindDirectedFallbackPose(
            rooms,
            occupiedPositions,
            players,
            preferredPlayer,
            random,
            out Vector3 fallbackPosition,
            out Quaternion fallbackRotation))
        {
            return false;
        }

        instance = SpawnEnemy(
            new EnemySelection(entry),
            fallbackPosition,
            fallbackRotation);
        if (instance != null)
            runEnemies.Add(instance);

        return instance != null;
    }

    internal void DespawnDirectedEnemy(GameObject instance)
    {
        if (instance == null)
            return;

        runEnemies.Remove(instance);
        if (CanSpawnAuthoritatively())
            DespawnEnemy(instance);
    }

    internal bool IsEnemyNearAnyPlayer(
        GameObject instance,
        PlayerStatus[] players,
        float distance)
    {
        if (instance == null || players == null)
            return false;

        float distanceSquared = Mathf.Max(0f, distance);
        distanceSquared *= distanceSquared;
        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus player = players[i];
            if (!EnemyTargeting.IsValidTarget(player, requireCanAct: false))
                continue;
            if ((player.transform.position - instance.transform.position).sqrMagnitude <=
                distanceSquared)
            {
                return true;
            }
        }

        return false;
    }

    internal bool CanSafelyDeactivateEnemy(
        GameObject instance,
        PlayerStatus[] players)
    {
        if (instance == null || players == null)
            return false;

        MonsterEncounterDirectorSettings settings = encounterDirectorSettings;
        settings.ClampValues();
        Vector3 position = instance.transform.position;
        float minimumDistanceSquared =
            settings.minimumDespawnDistance * settings.minimumDespawnDistance;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus player = players[i];
            if (!EnemyTargeting.IsValidTarget(player, requireCanAct: false))
                continue;
            if ((player.transform.position - position).sqrMagnitude <
                minimumDistanceSquared)
            {
                return false;
            }
            if (IsPositionVisibleToPlayer(position, instance, player, settings))
                return false;
        }

        return true;
    }

    List<Vector3> CollectActiveRunEnemyPositions()
    {
        List<Vector3> result = new List<Vector3>();
        for (int i = runEnemies.Count - 1; i >= 0; i--)
        {
            GameObject enemy = runEnemies[i];
            if (enemy == null)
            {
                runEnemies.RemoveAt(i);
                continue;
            }

            result.Add(enemy.transform.position);
        }

        return result;
    }

    bool TryFindDirectedFallbackPose(
        IReadOnlyList<GameObject> rooms,
        List<Vector3> occupiedPositions,
        PlayerStatus[] players,
        PlayerStatus preferredPlayer,
        System.Random random,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (rooms == null || rooms.Count == 0)
            return false;

        int attempts = Mathf.Max(1, fallbackPlacementAttemptsPerEnemy);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            GameObject room = rooms[random.Next(0, rooms.Count)];
            if (room == null)
                continue;

            RoomDefinition definition = GetRoomDefinition(room);
            Vector3 localCenter = definition != null
                ? definition.boundsCenter
                : Vector3.zero;
            Vector3 size = definition != null
                ? definition.size
                : new Vector3(20f, 4f, 20f);
            float halfWidth = Mathf.Max(1f, Mathf.Abs(size.x) * 0.45f);
            float halfDepth = Mathf.Max(1f, Mathf.Abs(size.z) * 0.45f);
            Vector3 localCandidate = localCenter + new Vector3(
                NextFloat(random, -halfWidth, halfWidth),
                0f,
                NextFloat(random, -halfDepth, halfDepth));
            Vector3 candidate = room.transform.TransformPoint(localCandidate);

            if (requireNavMeshPosition)
            {
                if (!NavMesh.SamplePosition(
                    candidate,
                    out NavMeshHit hit,
                    fallbackNavMeshSampleRadius,
                    navMeshAreaMask))
                {
                    continue;
                }

                candidate = hit.position;
            }

            if (!IsDirectedSpawnPoseSafe(
                candidate,
                occupiedPositions,
                players,
                preferredPlayer))
            {
                continue;
            }

            position = candidate;
            rotation = Quaternion.Euler(
                0f,
                NextFloat(random, 0f, 360f),
                0f);
            return true;
        }

        return false;
    }

    bool IsDirectedSpawnPoseSafe(
        Vector3 position,
        List<Vector3> occupiedPositions,
        PlayerStatus[] players,
        PlayerStatus preferredPlayer)
    {
        MonsterEncounterDirectorSettings settings = encounterDirectorSettings;
        settings.ClampValues();
        if (!IsFarEnoughFromRunEnemies(position, occupiedPositions))
            return false;
        if (!IsWithinSafePlayerDistance(
            position,
            players,
            preferredPlayer,
            settings))
        {
            return false;
        }
        if (!IsSpawnVolumeClear(position, settings))
            return false;
        if (settings.requireCompleteNavMeshPathToPlayer &&
            !CanReachAnyPlayer(position, players, settings))
        {
            return false;
        }

        for (int i = 0; i < players.Length; i++)
        {
            if (IsPositionVisibleToPlayer(position, null, players[i], settings))
                return false;
        }

        return true;
    }

    bool IsWithinSafePlayerDistance(
        Vector3 position,
        PlayerStatus[] players,
        PlayerStatus preferredPlayer,
        MonsterEncounterDirectorSettings settings)
    {
        float minimumSquared =
            settings.minimumSpawnDistance * settings.minimumSpawnDistance;
        float maximumSquared =
            settings.maximumSpawnDistance * settings.maximumSpawnDistance;
        bool withinMaximum = false;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus player = players[i];
            if (!EnemyTargeting.IsValidTarget(player, requireCanAct: false))
                continue;

            float squaredDistance =
                (player.transform.position - position).sqrMagnitude;
            if (squaredDistance < minimumSquared)
                return false;
            if (preferredPlayer == null || player == preferredPlayer)
                withinMaximum |= squaredDistance <= maximumSquared;
        }

        return withinMaximum;
    }

    bool IsSpawnVolumeClear(
        Vector3 position,
        MonsterEncounterDirectorSettings settings)
    {
        float radius = settings.clearanceRadius;
        Vector3 bottom = position + Vector3.up * (radius + 0.1f);
        Vector3 top = position + Vector3.up *
            Mathf.Max(radius + 0.1f, settings.clearanceHeight - radius);
        int hitCount = Physics.OverlapCapsuleNonAlloc(
            bottom,
            top,
            radius,
            spawnOverlapBuffer,
            settings.spawnBlockingLayers,
            QueryTriggerInteraction.Ignore);

        return hitCount == 0;
    }

    bool CanReachAnyPlayer(
        Vector3 position,
        PlayerStatus[] players,
        MonsterEncounterDirectorSettings settings)
    {
        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus player = players[i];
            if (!EnemyTargeting.IsValidTarget(player, requireCanAct: false))
                continue;
            if (!NavMesh.SamplePosition(
                player.transform.position,
                out NavMeshHit playerHit,
                settings.playerNavMeshSampleRadius,
                navMeshAreaMask))
            {
                continue;
            }

            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(
                position,
                playerHit.position,
                navMeshAreaMask,
                path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                return true;
            }
        }

        return false;
    }

    bool IsPositionVisibleToPlayer(
        Vector3 position,
        GameObject targetRoot,
        PlayerStatus player,
        MonsterEncounterDirectorSettings settings)
    {
        if (!EnemyTargeting.IsValidTarget(player, requireCanAct: false))
            return false;

        Vector3 eye = player.transform.position +
            Vector3.up * settings.playerEyeHeight;
        Vector3 target = position + Vector3.up * settings.monsterEyeHeight;
        Vector3 direction = target - eye;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
            return true;

        Vector3 flatDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
        Vector3 flatForward = Vector3.ProjectOnPlane(
            player.transform.forward,
            Vector3.up);
        if (flatDirection.sqrMagnitude > 0.001f &&
            flatForward.sqrMagnitude > 0.001f &&
            Vector3.Angle(flatForward, flatDirection) >
                settings.playerFieldOfView * 0.5f)
        {
            return false;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            eye,
            direction / distance,
            distance,
            settings.visibilityBlockingLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == null)
                continue;
            if (hitTransform == player.transform ||
                hitTransform.IsChildOf(player.transform))
            {
                continue;
            }
            if (targetRoot != null &&
                (hitTransform == targetRoot.transform ||
                 hitTransform.IsChildOf(targetRoot.transform)))
            {
                return true;
            }

            return false;
        }

        return true;
    }

    int CreateRunPlanSeed(int runSeed, int phase)
    {
        unchecked
        {
            int result = runSeed;
            result = result * 397 ^ seedOffset;
            result = result * 397 ^ Mathf.Max(1, phase);
            result = result * 397 ^ 0x5F3759DF;
            return result;
        }
    }

    bool TryChooseEnemy(
        RoomContentProfile contentProfile,
        RoomEnemyCategory allowedCategories,
        int roomIndex,
        System.Random random,
        out EnemySelection selected)
    {
        selected = new EnemySelection();

        RoomContentProfile.EnemyEntry entry;
        if (!contentProfile.TryChooseEnemy(
            allowedCategories,
            roomIndex,
            random,
            out entry))
        {
            return false;
        }

        selected = new EnemySelection(entry);
        return selected.prefab != null;
    }

    bool TryResolveNavMeshPose(
        Vector3 requestedPosition,
        Quaternion requestedRotation,
        out Vector3 resolvedPosition,
        out Quaternion resolvedRotation)
    {
        resolvedPosition = requestedPosition;
        resolvedRotation = requestedRotation;

        if (!requireNavMeshPosition)
            return true;

        NavMeshHit hit;
        if (!NavMesh.SamplePosition(
            requestedPosition,
            out hit,
            navMeshSampleRadius,
            navMeshAreaMask))
        {
            return false;
        }

        resolvedPosition = hit.position;
        return true;
    }

    GameObject SpawnEnemy(
        EnemySelection selection,
        Vector3 position,
        Quaternion rotation)
    {
        if (selection.prefab == null)
            return null;

        GameObject instance = Instantiate(selection.prefab, position, rotation);
        if (!instance.activeSelf)
            instance.SetActive(true);

        RegisterSpecialEnemySystems(instance);

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
                string enemyName = string.IsNullOrWhiteSpace(selection.label)
                    ? selection.prefab.name
                    : selection.label;

                Debug.LogWarning(
                    $"Room enemy '{enemyName}' needs a NetworkObject and NetworkManager registration for multiplayer spawning.");
            }

            UnregisterSpecialEnemySystems(instance);
            Destroy(instance);
            return null;
        }

        return instance;
    }

    void DespawnEnemy(GameObject instance)
    {
        if (instance == null) return;

        UnregisterSpecialEnemySystems(instance);

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                networkObject.Despawn(true);

            return;
        }

        Destroy(instance);
    }

    void RegisterSpecialEnemySystems(GameObject instance)
    {
        if (instance == null)
            return;

        TimeCamper timeCamper = instance.GetComponent<TimeCamper>();
        if (timeCamper != null && TimeCamperManager.Instance != null)
            TimeCamperManager.Instance.Register(timeCamper);
    }

    void UnregisterSpecialEnemySystems(GameObject instance)
    {
        if (instance == null)
            return;

        TimeCamper timeCamper = instance.GetComponent<TimeCamper>();
        if (timeCamper != null && TimeCamperManager.Instance != null)
            TimeCamperManager.Instance.Unregister(timeCamper);
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
        return spawnChanceMultiplier *
            Mathf.Max(0f, contentProfile.enemySpawnChanceMultiplier);
    }

    bool CanSpawnAuthoritatively()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (RegionRunState.HasSelectedRegion && RegionRunState.IsMultiplayer)
        {
            return networkManager != null &&
                networkManager.IsListening &&
                networkManager.IsServer;
        }

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

    void OnValidate()
    {
        if (encounterDirectorSettings == null)
            encounterDirectorSettings = new MonsterEncounterDirectorSettings();
        encounterDirectorSettings.ClampValues();

        fallbackPlacementAttemptsPerEnemy = Mathf.Max(
            1,
            fallbackPlacementAttemptsPerEnemy);
        minimumEnemySeparation = Mathf.Max(0f, minimumEnemySeparation);
        fallbackNavMeshSampleRadius = Mathf.Max(
            0.1f,
            fallbackNavMeshSampleRadius);
        navMeshSampleRadius = Mathf.Max(0.1f, navMeshSampleRadius);
    }
}
