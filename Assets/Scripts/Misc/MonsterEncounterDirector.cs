using System;
using System.Collections.Generic;
using UnityEngine;

public enum MonsterEncounterState
{
    Inactive,
    Active,
    Encounter,
    Disengaged
}

[Serializable]
public sealed class MonsterEncounterDirectorSettings
{
    [Tooltip("When disabled, the complete phase plan is spawned immediately using the legacy global behavior.")]
    public bool useDynamicEncounters = true;

    [Header("Encounter Capacity")]
    [Min(2)]
    [Tooltip("Maximum planned monsters physically active at once. Two is the safe minimum because support monsters need a companion.")]
    public int maximumSimultaneouslyActive = 2;

    [Min(1)]
    [Tooltip("Stops new activations while this many existing monsters are in an encounter.")]
    public int maximumSimultaneousEncounters = 1;

    [Header("Pacing")]
    [Min(0.1f)] public float evaluationInterval = 2f;
    [Min(0f)] public float initialActivationDelay = 10f;
    [Min(0f)] public float minimumSpawnAttemptInterval = 5f;
    [Min(0f)] public float encounterCooldown = 20f;
    [Range(0f, 1f)] public float baseActivationChance = 0.15f;
    [Min(0f)] public float quietPressureStartsAfter = 15f;
    [Min(0f)] public float guaranteedActivationAfter = 60f;
    [Min(0f)] public float isolatedPlayerDistance = 18f;
    [Range(0f, 1f)] public float isolatedPlayerChanceBonus = 0.15f;

    [Header("Active Lifetime")]
    [Min(0f)] public float minimumActiveDuration = 25f;
    [Min(0f)] public float maximumActiveDuration = 80f;
    [Min(0f)] public float encounterDetectionDistance = 16f;
    [Min(0f)] public float encounterDisengageDistance = 25f;
    [Min(0f)] public float disengageGracePeriod = 8f;
    [Min(0f)] public float despawnDelayAfterDisengage = 8f;
    [Min(0f)] public float inactiveSlotCooldown = 20f;
    [Min(0f)] public float minimumDespawnDistance = 16f;

    [Header("Safe Spawn")]
    [Min(0f)] public float minimumSpawnDistance = 14f;
    [Min(0f)] public float maximumSpawnDistance = 65f;
    [Range(1f, 179f)] public float playerFieldOfView = 100f;
    [Min(0f)] public float playerEyeHeight = 1.6f;
    [Min(0f)] public float monsterEyeHeight = 1f;
    public LayerMask visibilityBlockingLayers = ~0;

    [Min(0.1f)] public float clearanceRadius = 0.65f;
    [Min(0.2f)] public float clearanceHeight = 2f;
    public LayerMask spawnBlockingLayers = ~0;
    public bool requireCompleteNavMeshPathToPlayer = true;
    [Min(0.1f)] public float playerNavMeshSampleRadius = 3f;

    [Header("Debug")]
    public bool logDirectorEvents = true;

    public void ClampValues()
    {
        maximumSimultaneouslyActive = Mathf.Max(2, maximumSimultaneouslyActive);
        maximumSimultaneousEncounters = Mathf.Max(1, maximumSimultaneousEncounters);
        evaluationInterval = Mathf.Max(0.1f, evaluationInterval);
        initialActivationDelay = Mathf.Max(0f, initialActivationDelay);
        minimumSpawnAttemptInterval = Mathf.Max(0f, minimumSpawnAttemptInterval);
        encounterCooldown = Mathf.Max(0f, encounterCooldown);
        quietPressureStartsAfter = Mathf.Max(0f, quietPressureStartsAfter);
        guaranteedActivationAfter = Mathf.Max(
            quietPressureStartsAfter,
            guaranteedActivationAfter);
        isolatedPlayerDistance = Mathf.Max(0f, isolatedPlayerDistance);
        minimumActiveDuration = Mathf.Max(0f, minimumActiveDuration);
        maximumActiveDuration = Mathf.Max(
            minimumActiveDuration,
            maximumActiveDuration);
        encounterDetectionDistance = Mathf.Max(0f, encounterDetectionDistance);
        encounterDisengageDistance = Mathf.Max(
            encounterDetectionDistance,
            encounterDisengageDistance);
        disengageGracePeriod = Mathf.Max(0f, disengageGracePeriod);
        despawnDelayAfterDisengage = Mathf.Max(0f, despawnDelayAfterDisengage);
        inactiveSlotCooldown = Mathf.Max(0f, inactiveSlotCooldown);
        minimumDespawnDistance = Mathf.Max(0f, minimumDespawnDistance);
        minimumSpawnDistance = Mathf.Max(0f, minimumSpawnDistance);
        maximumSpawnDistance = Mathf.Max(minimumSpawnDistance, maximumSpawnDistance);
        playerEyeHeight = Mathf.Max(0f, playerEyeHeight);
        monsterEyeHeight = Mathf.Max(0f, monsterEyeHeight);
        clearanceRadius = Mathf.Max(0.1f, clearanceRadius);
        clearanceHeight = Mathf.Max(clearanceRadius * 2f, clearanceHeight);
        playerNavMeshSampleRadius = Mathf.Max(0.1f, playerNavMeshSampleRadius);
    }
}

[DisallowMultipleComponent]
public sealed class MonsterEncounterDirector : MonoBehaviour
{
    sealed class MonsterSlot
    {
        public int slotId;
        public RoomContentProfile.EnemyEntry entry;
        public GameObject instance;
        public MonsterEncounterState state;
        public float activatedAt;
        public float stateChangedAt;
        public float lastPlayerNearAt;
        public float deactivateAt;
        public float eligibleAt;
        public int activationCount;
    }

    RoomEnemySpawner owner;
    readonly List<MonsterSlot> slots = new List<MonsterSlot>();
    readonly List<GameObject> rooms = new List<GameObject>();
    System.Random random;
    bool initialized;
    float initializedAt;
    float nextEvaluationAt;
    float lastSpawnAt = float.NegativeInfinity;
    float lastEncounterEndedAt = float.NegativeInfinity;
    float lastEncounterActivityAt;

    [Header("Runtime Debug (read only)")]
    [SerializeField] int plannedMonsterCount;
    [SerializeField] int inactiveMonsterCount;
    [SerializeField] int activeMonsterCount;
    [SerializeField] int encounterMonsterCount;
    [SerializeField] int disengagedMonsterCount;
    [SerializeField] string lastDirectorEvent = string.Empty;

    public bool HasPlan => initialized;

    public void Configure(RoomEnemySpawner spawner)
    {
        owner = spawner;
    }

    public bool InitializePlan(
        RoomContentProfile profile,
        IReadOnlyList<int> selectedEntryIndices,
        IReadOnlyList<GameObject> generatedRooms,
        int planSeed,
        int phase)
    {
        if (owner == null || profile == null || selectedEntryIndices == null)
            return false;

        ResetRun();
        rooms.Clear();
        if (generatedRooms != null)
        {
            for (int i = 0; i < generatedRooms.Count; i++)
            {
                if (generatedRooms[i] != null)
                    rooms.Add(generatedRooms[i]);
            }
        }

        for (int i = 0; i < selectedEntryIndices.Count; i++)
        {
            int entryIndex = selectedEntryIndices[i];
            if (profile.enemies == null ||
                entryIndex < 0 ||
                entryIndex >= profile.enemies.Length)
            {
                continue;
            }

            RoomContentProfile.EnemyEntry entry = profile.enemies[entryIndex];
            if (entry == null || entry.prefab == null)
                continue;

            slots.Add(new MonsterSlot
            {
                slotId = slots.Count,
                entry = entry,
                state = MonsterEncounterState.Inactive
            });
        }

        if (slots.Count != selectedEntryIndices.Count || slots.Count == 0)
        {
            slots.Clear();
            rooms.Clear();
            UpdateDebugCounters();
            return false;
        }

        MonsterEncounterDirectorSettings settings = Settings;
        settings.ClampValues();
        random = new System.Random(unchecked(planSeed ^ 0x45D9F3B));
        initialized = true;
        initializedAt = Time.time;
        lastEncounterActivityAt = initializedAt;
        lastSpawnAt = initializedAt - settings.minimumSpawnAttemptInterval;
        nextEvaluationAt = initializedAt + settings.initialActivationDelay;
        plannedMonsterCount = slots.Count;
        RecordEvent(
            $"Encounter Director prepared phase {phase}: {slots.Count} available, " +
            $"up to {Mathf.Min(slots.Count, settings.maximumSimultaneouslyActive)} active.");
        UpdateDebugCounters();
        return true;
    }

    public void ResetRun()
    {
        if (owner != null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].instance != null)
                    owner.DespawnDirectedEnemy(slots[i].instance);
            }
        }

        slots.Clear();
        rooms.Clear();
        initialized = false;
        random = null;
        lastSpawnAt = float.NegativeInfinity;
        lastEncounterEndedAt = float.NegativeInfinity;
        lastEncounterActivityAt = 0f;
        nextEvaluationAt = 0f;
        plannedMonsterCount = 0;
        lastDirectorEvent = string.Empty;
        UpdateDebugCounters();
    }

    void Update()
    {
        if (!initialized || owner == null || !owner.CanRunEncounterDirector())
            return;
        if (Time.time < nextEvaluationAt)
            return;

        MonsterEncounterDirectorSettings settings = Settings;
        settings.ClampValues();
        nextEvaluationAt = Time.time + settings.evaluationInterval;

        PlayerStatus[] players = FindValidPlayers();
        RefreshDestroyedSlots(Time.time, settings);
        UpdateActiveStates(players, Time.time, settings);
        TryActivateNextEncounter(players, Time.time, settings);
        UpdateDebugCounters();
    }

    void RefreshDestroyedSlots(
        float now,
        MonsterEncounterDirectorSettings settings)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            MonsterSlot slot = slots[i];
            if (slot.state == MonsterEncounterState.Inactive || slot.instance != null)
                continue;

            slot.instance = null;
            slot.state = MonsterEncounterState.Inactive;
            slot.stateChangedAt = now;
            slot.eligibleAt = now + settings.inactiveSlotCooldown;
            RecordEvent($"{GetSlotName(slot)} became inactive after leaving the map.");
        }
    }

    void UpdateActiveStates(
        PlayerStatus[] players,
        float now,
        MonsterEncounterDirectorSettings settings)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            MonsterSlot slot = slots[i];
            if (slot.state == MonsterEncounterState.Inactive || slot.instance == null)
                continue;

            bool closeForEncounter = owner.IsEnemyNearAnyPlayer(
                slot.instance,
                players,
                settings.encounterDetectionDistance);
            bool closeForDisengage = owner.IsEnemyNearAnyPlayer(
                slot.instance,
                players,
                settings.encounterDisengageDistance);

            if (closeForEncounter)
            {
                slot.lastPlayerNearAt = now;
                lastEncounterActivityAt = now;
                if (slot.state != MonsterEncounterState.Encounter)
                    ChangeState(slot, MonsterEncounterState.Encounter, now);

                continue;
            }

            if (slot.state == MonsterEncounterState.Encounter)
            {
                if (closeForDisengage)
                {
                    slot.lastPlayerNearAt = now;
                    lastEncounterActivityAt = now;
                    continue;
                }

                if (now - slot.lastPlayerNearAt >= settings.disengageGracePeriod)
                {
                    ChangeState(slot, MonsterEncounterState.Disengaged, now);
                    lastEncounterEndedAt = now;
                }

                continue;
            }

            if (slot.state == MonsterEncounterState.Active && now >= slot.deactivateAt)
                ChangeState(slot, MonsterEncounterState.Disengaged, now);

            if (IsReadyToDeactivate(slot, now, settings))
                TryDeactivateSlot(slot, players, now, settings);
        }
    }

    void TryActivateNextEncounter(
        PlayerStatus[] players,
        float now,
        MonsterEncounterDirectorSettings settings)
    {
        if (players.Length == 0 || random == null)
            return;

        List<MonsterSlot> available = GetAvailableSlots(now);
        int activeCount = CountPhysicalSlots();
        int encounterCount = CountState(MonsterEncounterState.Encounter);
        int activeLimit = Mathf.Min(slots.Count, settings.maximumSimultaneouslyActive);
        double sinceEncounterEnded = float.IsNegativeInfinity(lastEncounterEndedAt)
            ? double.PositiveInfinity
            : now - lastEncounterEndedAt;

        if (!EncounterDirectorPolicy.CanAttemptActivation(
            available.Count,
            activeCount,
            encounterCount,
            activeLimit,
            settings.maximumSimultaneousEncounters,
            now - lastSpawnAt,
            settings.minimumSpawnAttemptInterval,
            sinceEncounterEnded,
            settings.encounterCooldown))
        {
            return;
        }

        PlayerStatus preferredPlayer = FindPreferredPlayer(
            players,
            settings.isolatedPlayerDistance,
            out bool hasIsolatedPlayer);
        double chance = EncounterDirectorPolicy.CalculateActivationChance(
            settings.baseActivationChance,
            now - lastEncounterActivityAt,
            settings.quietPressureStartsAfter,
            settings.guaranteedActivationAfter,
            hasIsolatedPlayer,
            settings.isolatedPlayerChanceBonus);

        if (random.NextDouble() > chance)
            return;

        List<MonsterSlot> activationGroup = BuildActivationGroup(
            available,
            activeCount,
            activeLimit);
        if (activationGroup.Count == 0)
            return;

        lastSpawnAt = now;
        List<MonsterSlot> activated = new List<MonsterSlot>();
        for (int i = 0; i < activationGroup.Count; i++)
        {
            MonsterSlot slot = activationGroup[i];
            if (!owner.TrySpawnDirectedEnemy(
                slot.entry,
                rooms,
                players,
                preferredPlayer,
                random,
                out GameObject instance))
            {
                RollBackActivation(activated, now, settings);
                return;
            }

            slot.instance = instance;
            slot.activatedAt = now;
            slot.stateChangedAt = now;
            slot.lastPlayerNearAt = now;
            slot.deactivateAt = now + RandomRange(
                settings.minimumActiveDuration,
                settings.maximumActiveDuration);
            slot.activationCount++;
            ChangeState(slot, MonsterEncounterState.Active, now);
            activated.Add(slot);
        }

        RecordEvent($"Activated: {JoinSlotNames(activated)}.");
    }

    List<MonsterSlot> BuildActivationGroup(
        List<MonsterSlot> available,
        int activeCount,
        int activeLimit)
    {
        List<MonsterSlot> result = new List<MonsterSlot>();
        if (available.Count == 0 || activeCount >= activeLimit)
            return result;

        MonsterSlot selected = null;
        if (activeCount == 0)
        {
            List<MonsterSlot> independent = available.FindAll(
                slot => slot.entry != null && !slot.entry.requiresCompanion);
            if (independent.Count > 0)
                selected = independent[random.Next(0, independent.Count)];
        }

        if (selected == null)
            selected = available[random.Next(0, available.Count)];

        result.Add(selected);
        if (!selected.entry.requiresCompanion || activeCount > 0)
            return result;

        if (activeCount + 2 > activeLimit)
        {
            result.Clear();
            return result;
        }

        List<MonsterSlot> companions = available.FindAll(slot => slot != selected);
        if (companions.Count == 0)
        {
            result.Clear();
            return result;
        }

        result.Add(companions[random.Next(0, companions.Count)]);
        return result;
    }

    void RollBackActivation(
        List<MonsterSlot> activated,
        float now,
        MonsterEncounterDirectorSettings settings)
    {
        for (int i = 0; i < activated.Count; i++)
            DeactivateSlot(activated[i], now, settings, "activation rollback");
    }

    void TryDeactivateSlot(
        MonsterSlot slot,
        PlayerStatus[] players,
        float now,
        MonsterEncounterDirectorSettings settings)
    {
        if (slot.instance == null ||
            !owner.CanSafelyDeactivateEnemy(slot.instance, players))
        {
            return;
        }

        List<MonsterSlot> physical = GetPhysicalSlots();
        if (physical.Count == 2)
        {
            MonsterSlot remaining = physical[0] == slot ? physical[1] : physical[0];
            if (remaining.entry.requiresCompanion)
            {
                if (!IsReadyToDeactivate(remaining, now, settings) ||
                    remaining.instance == null ||
                    !owner.CanSafelyDeactivateEnemy(remaining.instance, players))
                {
                    return;
                }

                DeactivateSlot(slot, now, settings, "disengaged");
                DeactivateSlot(remaining, now, settings, "companion left");
                return;
            }
        }

        DeactivateSlot(slot, now, settings, "disengaged");
    }

    void DeactivateSlot(
        MonsterSlot slot,
        float now,
        MonsterEncounterDirectorSettings settings,
        string reason)
    {
        if (slot == null)
            return;

        if (slot.instance != null)
            owner.DespawnDirectedEnemy(slot.instance);

        slot.instance = null;
        slot.state = MonsterEncounterState.Inactive;
        slot.stateChangedAt = now;
        slot.eligibleAt = now + settings.inactiveSlotCooldown;
        RecordEvent($"Deactivated {GetSlotName(slot)} ({reason}).");
    }

    bool IsReadyToDeactivate(
        MonsterSlot slot,
        float now,
        MonsterEncounterDirectorSettings settings)
    {
        return slot != null &&
            slot.state == MonsterEncounterState.Disengaged &&
            now - slot.activatedAt >= settings.minimumActiveDuration &&
            now - slot.stateChangedAt >= settings.despawnDelayAfterDisengage;
    }

    void ChangeState(
        MonsterSlot slot,
        MonsterEncounterState nextState,
        float now)
    {
        if (slot.state == nextState)
            return;

        MonsterEncounterState previous = slot.state;
        slot.state = nextState;
        slot.stateChangedAt = now;
        RecordEvent($"{GetSlotName(slot)}: {previous} -> {nextState}.");
    }

    List<MonsterSlot> GetAvailableSlots(float now)
    {
        List<MonsterSlot> result = new List<MonsterSlot>();
        for (int i = 0; i < slots.Count; i++)
        {
            MonsterSlot slot = slots[i];
            if (slot.state == MonsterEncounterState.Inactive &&
                slot.instance == null &&
                now >= slot.eligibleAt)
            {
                result.Add(slot);
            }
        }

        return result;
    }

    List<MonsterSlot> GetPhysicalSlots()
    {
        List<MonsterSlot> result = new List<MonsterSlot>();
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].state != MonsterEncounterState.Inactive &&
                slots[i].instance != null)
            {
                result.Add(slots[i]);
            }
        }

        return result;
    }

    int CountPhysicalSlots()
    {
        int count = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].state != MonsterEncounterState.Inactive &&
                slots[i].instance != null)
            {
                count++;
            }
        }

        return count;
    }

    int CountState(MonsterEncounterState state)
    {
        int count = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].instance != null && slots[i].state == state)
                count++;
        }

        return count;
    }

    void UpdateDebugCounters()
    {
        plannedMonsterCount = slots.Count;
        inactiveMonsterCount = 0;
        activeMonsterCount = 0;
        encounterMonsterCount = 0;
        disengagedMonsterCount = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            switch (slots[i].state)
            {
                case MonsterEncounterState.Inactive:
                    inactiveMonsterCount++;
                    break;
                case MonsterEncounterState.Active:
                    activeMonsterCount++;
                    break;
                case MonsterEncounterState.Encounter:
                    encounterMonsterCount++;
                    break;
                case MonsterEncounterState.Disengaged:
                    disengagedMonsterCount++;
                    break;
            }
        }
    }

    PlayerStatus[] FindValidPlayers()
    {
        PlayerStatus[] found = FindObjectsByType<PlayerStatus>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        List<PlayerStatus> valid = new List<PlayerStatus>();
        for (int i = 0; i < found.Length; i++)
        {
            if (EnemyTargeting.IsValidTarget(found[i], requireCanAct: false))
                valid.Add(found[i]);
        }

        return valid.ToArray();
    }

    PlayerStatus FindPreferredPlayer(
        PlayerStatus[] players,
        float isolationDistance,
        out bool hasIsolatedPlayer)
    {
        hasIsolatedPlayer = false;
        if (players == null || players.Length == 0)
            return null;
        if (players.Length == 1)
            return players[0];

        PlayerStatus mostIsolated = players[0];
        float largestNearestDistance = 0f;
        for (int i = 0; i < players.Length; i++)
        {
            float nearest = float.PositiveInfinity;
            for (int otherIndex = 0; otherIndex < players.Length; otherIndex++)
            {
                if (otherIndex == i)
                    continue;

                float distance = Vector3.Distance(
                    players[i].transform.position,
                    players[otherIndex].transform.position);
                nearest = Mathf.Min(nearest, distance);
            }

            if (nearest > largestNearestDistance)
            {
                largestNearestDistance = nearest;
                mostIsolated = players[i];
            }
        }

        hasIsolatedPlayer = largestNearestDistance >= isolationDistance;
        return hasIsolatedPlayer
            ? mostIsolated
            : players[random.Next(0, players.Length)];
    }

    float RandomRange(float minimum, float maximum)
    {
        if (maximum <= minimum)
            return minimum;

        return minimum + (float)random.NextDouble() * (maximum - minimum);
    }

    string GetSlotName(MonsterSlot slot)
    {
        if (slot == null || slot.entry == null)
            return "Monster";
        if (!string.IsNullOrWhiteSpace(slot.entry.label))
            return slot.entry.label;

        return slot.entry.prefab != null ? slot.entry.prefab.name : "Monster";
    }

    string JoinSlotNames(List<MonsterSlot> values)
    {
        List<string> names = new List<string>();
        for (int i = 0; i < values.Count; i++)
            names.Add(GetSlotName(values[i]));

        return string.Join(", ", names);
    }

    void RecordEvent(string message)
    {
        lastDirectorEvent = message;
        if (Settings.logDirectorEvents)
            Debug.Log(message, this);
    }

    MonsterEncounterDirectorSettings Settings
    {
        get
        {
            if (owner != null && owner.encounterDirectorSettings != null)
                return owner.encounterDirectorSettings;

            return fallbackSettings;
        }
    }

    readonly MonsterEncounterDirectorSettings fallbackSettings =
        new MonsterEncounterDirectorSettings();
}
