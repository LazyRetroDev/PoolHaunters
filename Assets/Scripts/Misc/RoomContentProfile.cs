using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "RoomContentProfile",
    menuName = "Pool Haunters/Rooms/Room Content Profile")]
public class RoomContentProfile : ScriptableObject
{
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

    [Serializable]
    public class EnemyEntry
    {
        public string label;
        public GameObject prefab;
        public RoomEnemyCategory category = RoomEnemyCategory.Common;

        [Tooltip("Difficulty used by the global phase enemy planner.")]
        public RunEnemyDifficulty difficulty = RunEnemyDifficulty.Easy;

        [Tooltip("Support enemies cannot be the only creature selected for a run.")]
        public bool requiresCompanion;

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

    [Header("Resources")]
    public ResourceEntry[] resources;

    [Header("Enemies")]
    public EnemyEntry[] enemies;

    [Header("Spawn Behavior")]
    [Range(0f, 2f)]
    public float spawnChanceMultiplier = 1f;

    [Range(0f, 2f)]
    public float enemySpawnChanceMultiplier = 1f;

    public bool HasResourceTable
    {
        get { return resources != null && resources.Length > 0; }
    }

    public bool HasEnemyTable
    {
        get { return enemies != null && enemies.Length > 0; }
    }

    public bool TryChooseResource(
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
            if (!CanUseResource(entry, allowedCategories, roomIndex))
                continue;

            totalWeight += entry.weight;
        }

        if (totalWeight <= 0f)
            return false;

        double roll = random.NextDouble() * totalWeight;
        for (int i = 0; i < resources.Length; i++)
        {
            ResourceEntry entry = resources[i];
            if (!CanUseResource(entry, allowedCategories, roomIndex))
                continue;

            roll -= entry.weight;
            if (roll <= 0d)
            {
                selected = entry;
                return true;
            }
        }

        return false;
    }

    public bool TryChooseEnemy(
        RoomEnemyCategory allowedCategories,
        int roomIndex,
        System.Random random,
        out EnemyEntry selected)
    {
        selected = null;

        if (enemies == null || enemies.Length == 0)
            return false;

        float totalWeight = 0f;
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyEntry entry = enemies[i];
            if (!CanUseEnemy(entry, allowedCategories, roomIndex))
                continue;

            totalWeight += entry.weight;
        }

        if (totalWeight <= 0f)
            return false;

        double roll = random.NextDouble() * totalWeight;
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyEntry entry = enemies[i];
            if (!CanUseEnemy(entry, allowedCategories, roomIndex))
                continue;

            roll -= entry.weight;
            if (roll <= 0d)
            {
                selected = entry;
                return true;
            }
        }

        return false;
    }

    bool CanUseResource(
        ResourceEntry entry,
        RoomResourceCategory allowedCategories,
        int roomIndex)
    {
        return entry != null &&
            entry.IsAvailableForRoom(roomIndex) &&
            AllowsCategory(allowedCategories, entry.category);
    }

    bool CanUseEnemy(
        EnemyEntry entry,
        RoomEnemyCategory allowedCategories,
        int roomIndex)
    {
        return entry != null &&
            entry.IsAvailableForRoom(roomIndex) &&
            AllowsCategory(allowedCategories, entry.category);
    }

    bool AllowsCategory(
        RoomResourceCategory allowedCategories,
        RoomResourceCategory category)
    {
        return category != RoomResourceCategory.None &&
            (allowedCategories & category) != RoomResourceCategory.None;
    }

    bool AllowsCategory(
        RoomEnemyCategory allowedCategories,
        RoomEnemyCategory category)
    {
        return category != RoomEnemyCategory.None &&
            (allowedCategories & category) != RoomEnemyCategory.None;
    }

    void OnValidate()
    {
        spawnChanceMultiplier = Mathf.Max(0f, spawnChanceMultiplier);
        enemySpawnChanceMultiplier = Mathf.Max(0f, enemySpawnChanceMultiplier);

        ValidateResources();
        ValidateEnemies();
    }

    void ValidateResources()
    {
        if (resources == null)
            return;

        for (int i = 0; i < resources.Length; i++)
        {
            ResourceEntry entry = resources[i];
            if (entry == null)
                continue;

            entry.weight = Mathf.Max(0f, entry.weight);
            entry.minimumRoomIndex = Mathf.Max(0, entry.minimumRoomIndex);
            if (entry.maximumRoomIndex < -1)
                entry.maximumRoomIndex = -1;
        }
    }

    void ValidateEnemies()
    {
        if (enemies == null)
            return;

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyEntry entry = enemies[i];
            if (entry == null)
                continue;

            entry.weight = Mathf.Max(0f, entry.weight);
            entry.minimumRoomIndex = Mathf.Max(0, entry.minimumRoomIndex);
            if (entry.maximumRoomIndex < -1)
                entry.maximumRoomIndex = -1;
        }
    }
}
