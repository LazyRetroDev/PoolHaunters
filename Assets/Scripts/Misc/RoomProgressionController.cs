using System;
using UnityEngine;

[Serializable]
public class RoomProgressionRule
{
    public string label = "Rule";

    [Min(0)]
    public int minimumRoomIndex;

    [Tooltip("Use -1 for no maximum.")]
    public int maximumRoomIndex = -1;

    [Tooltip("When enabled, this rule applies to the last room slot based on RoomGenerator.maxGeneratedRooms.")]
    public bool appliesToFinalRoom;

    public RoomCategory[] allowedCategories = new RoomCategory[0];

    public bool Matches(int roomIndex, int maxRoomCount)
    {
        if (appliesToFinalRoom)
            return maxRoomCount > 0 && roomIndex >= Mathf.Max(0, maxRoomCount - 1);

        if (roomIndex < minimumRoomIndex)
            return false;

        return maximumRoomIndex < 0 || roomIndex <= maximumRoomIndex;
    }

    public bool Allows(RoomCategory category)
    {
        if (allowedCategories == null || allowedCategories.Length == 0)
            return true;

        for (int i = 0; i < allowedCategories.Length; i++)
        {
            if (allowedCategories[i] == category)
                return true;
        }

        return false;
    }
}

[DisallowMultipleComponent]
public class RoomProgressionController : MonoBehaviour
{
    [Header("Rules")]
    public bool useProgressionRules = true;
    public bool allowRoomsWithoutDefinition = true;
    public bool allowAnyWhenNoRuleMatches = true;

    [Tooltip("Keeps the run from blocking while room prefabs are still being categorized.")]
    public bool fallbackToAnyCategoryWhenNoPrefabMatches = true;

    public RoomProgressionRule[] rules = new RoomProgressionRule[0];

    public bool HasActiveRules
    {
        get { return useProgressionRules && rules != null && rules.Length > 0; }
    }

    public bool ShouldFallbackWhenNoPrefabMatches
    {
        get { return HasActiveRules && fallbackToAnyCategoryWhenNoPrefabMatches; }
    }

    public bool AllowsRoom(RoomDefinition definition, int roomIndex, int maxRoomCount)
    {
        if (!HasActiveRules)
            return true;

        if (definition == null)
            return allowRoomsWithoutDefinition;

        RoomProgressionRule rule = GetRuleForRoom(roomIndex, maxRoomCount);
        if (rule == null)
            return allowAnyWhenNoRuleMatches;

        return rule.Allows(definition.category);
    }

    public RoomProgressionRule GetRuleForRoom(int roomIndex, int maxRoomCount)
    {
        if (!HasActiveRules)
            return null;

        for (int i = 0; i < rules.Length; i++)
        {
            RoomProgressionRule rule = rules[i];
            if (rule != null && rule.appliesToFinalRoom && rule.Matches(roomIndex, maxRoomCount))
                return rule;
        }

        for (int i = 0; i < rules.Length; i++)
        {
            RoomProgressionRule rule = rules[i];
            if (rule != null && !rule.appliesToFinalRoom && rule.Matches(roomIndex, maxRoomCount))
                return rule;
        }

        return null;
    }

    public string GetRuleLabel(int roomIndex, int maxRoomCount)
    {
        RoomProgressionRule rule = GetRuleForRoom(roomIndex, maxRoomCount);
        if (rule == null)
            return "No matching rule";

        return string.IsNullOrWhiteSpace(rule.label) ? "Unnamed rule" : rule.label;
    }

    [ContextMenu("Use Default Progression")]
    public void UseDefaultProgression()
    {
        rules = CreateDefaultRules();
    }

    void Reset()
    {
        UseDefaultProgression();
    }

    void OnValidate()
    {
        if (rules == null)
            return;

        for (int i = 0; i < rules.Length; i++)
        {
            RoomProgressionRule rule = rules[i];
            if (rule == null) continue;

            rule.minimumRoomIndex = Mathf.Max(0, rule.minimumRoomIndex);
            if (rule.maximumRoomIndex < -1)
                rule.maximumRoomIndex = -1;

            if (!rule.appliesToFinalRoom &&
                rule.maximumRoomIndex >= 0 &&
                rule.maximumRoomIndex < rule.minimumRoomIndex)
            {
                rule.maximumRoomIndex = rule.minimumRoomIndex;
            }

            if (string.IsNullOrWhiteSpace(rule.label))
                rule.label = rule.appliesToFinalRoom ? "Final" : "Rule";
        }
    }

    static RoomProgressionRule[] CreateDefaultRules()
    {
        return new[]
        {
            new RoomProgressionRule
            {
                label = "Start",
                minimumRoomIndex = 0,
                maximumRoomIndex = 0,
                allowedCategories = new[]
                {
                    RoomCategory.SubmarineSpawn,
                    RoomCategory.Corridor,
                    RoomCategory.Common
                }
            },
            new RoomProgressionRule
            {
                label = "Early Run",
                minimumRoomIndex = 1,
                maximumRoomIndex = 1,
                allowedCategories = new[]
                {
                    RoomCategory.Corridor,
                    RoomCategory.Common,
                    RoomCategory.Water
                }
            },
            new RoomProgressionRule
            {
                label = "Mid Run",
                minimumRoomIndex = 2,
                maximumRoomIndex = -1,
                allowedCategories = new[]
                {
                    RoomCategory.Corridor,
                    RoomCategory.Common,
                    RoomCategory.Special,
                    RoomCategory.Water,
                    RoomCategory.Pool
                }
            },
            new RoomProgressionRule
            {
                label = "Final",
                appliesToFinalRoom = true,
                allowedCategories = new[]
                {
                    RoomCategory.Final
                }
            }
        };
    }
}
