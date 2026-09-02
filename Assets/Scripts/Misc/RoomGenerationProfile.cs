using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "RoomGenerationProfile",
    menuName = "Pool Haunters/Rooms/Room Generation Profile")]
public class RoomGenerationProfile : ScriptableObject
{
    [Header("Rooms")]
    public bool overrideRoomPrefabs;
    public GameObject[] roomPrefabs = new GameObject[0];
    [Min(1)] public int startingRoomCount = 2;
    [Min(0)] public int maxGeneratedRooms;

    [Header("Full Map Generation")]
    public bool generateFullMapOnStart = true;

    [Header("Branches")]
    [Min(1)] public int minimumBranchCount = 3;
    [Min(1)] public int maximumBranchCount = 4;
    [Min(1)] public int minimumRoomsPerBranch = 3;
    [Min(1)] public int maximumRoomsPerBranch = 6;
    [Min(1)] public int branchGenerationAttempts = 8;
    public bool guaranteeMinimumBranchCount = true;
    public bool allowRepeatingFinalRoomsForBranches = true;

    [Header("Branch Connections")]
    public bool connectAdjacentBranches = true;
    [Range(0f, 1f)] public float adjacentBranchConnectionChance = 0.35f;
    [Tooltip("Use 0 for no maximum.")]
    [Min(0)] public int maximumAdjacentBranchConnections;
    [Tooltip("Maximum world distance allowed between DoorPoints when connecting two already generated branches.")]
    [Min(0.1f)] public float maximumAdjacentBranchConnectionDoorDistance = 4.5f;
    public bool enforceFinalRoomMinimumDistance = true;

    [Header("Validation")]
    public bool validateFullMapAfterGeneration = true;
    [Min(1)] public int fullMapGenerationAttempts = 20;
    public bool validateClosedDoorObjects = true;
    public bool validateConnectedNavMeshLinks = true;

    [Header("Backtracking")]
    public bool keepGeneratedRoomsForBacktracking = true;
    [Min(0)] public int roomsToKeep;

    [Header("Seed")]
    public bool overrideSeed;
    public int seed;
    public bool useSelectedRunSeed = true;
    public bool randomizeSeedWhenNoRunSelected;

    [Header("Enemy Setup")]
    public bool spawnTimeCamperAfterStartingRooms = true;

    [Header("Progression")]
    public bool applyProgressionSettings;
    public bool useProgressionRules = true;
    public bool allowRoomsWithoutDefinition = true;
    public bool allowAnyWhenNoRuleMatches = true;
    public bool fallbackToAnyCategoryWhenNoPrefabMatches = true;
    public RoomProgressionRule[] progressionRules = new RoomProgressionRule[0];

    [Header("Placement")]
    public bool useGridOccupancy = true;
    public bool useBoundsOverlapCheck = true;
    [Min(0f)] public float roomBoundsInset = 0.25f;
    [Min(1)] public int placementAttempts = 8;

    [Header("Doorway Validation")]
    public bool validateConnectedDoorwayClearance = true;
    [Min(0.1f)] public float doorwayClearanceWidth = 1.2f;
    [Min(0.1f)] public float doorwayClearanceHeight = 4.5f;
    [Min(0.1f)] public float doorwayClearanceDepth = 2f;
    public bool ignoreFloorLevelDoorwayBlockers = true;
    [Min(0f)] public float doorwayFloorBlockerTolerance = 0.1f;
    public LayerMask doorwayIgnoredLayers = 1 << 7;
    public LayerMask doorwayBlockingLayers = ~0;

    [Header("NavMesh Links")]
    public bool createNavMeshLinksBetweenRooms = true;
    [Min(0.1f)] public float navMeshLinkWidth = 2f;
    [Min(0f)] public float navMeshLinkWorldHeight = 0.75f;
    [Min(0.1f)] public float navMeshLinkHalfLength = 2f;

    [Header("Debug")]
    public bool logGenerationReport = true;
    public bool logRejectedFullMapAttempts = true;
    public bool renameGeneratedRoomsForDebug = true;
    public bool drawGeneratedMapGizmos = true;
    public bool drawGeneratedMapLabels = true;
    public bool drawClosedConnectorGizmos = true;
    public bool drawBranchConnectionGizmos = true;
    [Min(0.1f)] public float generationDebugMarkerSize = 1.25f;
    [Min(0f)] public float generationDebugLabelHeight = 2.5f;

    public void ApplyProgressionTo(RoomProgressionController progression)
    {
        if (!applyProgressionSettings || progression == null)
            return;

        progression.useProgressionRules = useProgressionRules;
        progression.allowRoomsWithoutDefinition = allowRoomsWithoutDefinition;
        progression.allowAnyWhenNoRuleMatches = allowAnyWhenNoRuleMatches;
        progression.fallbackToAnyCategoryWhenNoPrefabMatches =
            fallbackToAnyCategoryWhenNoPrefabMatches;
        progression.rules = CloneProgressionRules(progressionRules);
    }

    public RoomProgressionRule[] CloneProgressionRules()
    {
        return CloneProgressionRules(progressionRules);
    }

    static RoomProgressionRule[] CloneProgressionRules(RoomProgressionRule[] source)
    {
        if (source == null)
            return new RoomProgressionRule[0];

        RoomProgressionRule[] clone = new RoomProgressionRule[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            RoomProgressionRule rule = source[i];
            if (rule == null)
                continue;

            clone[i] = new RoomProgressionRule
            {
                label = rule.label,
                minimumRoomIndex = rule.minimumRoomIndex,
                maximumRoomIndex = rule.maximumRoomIndex,
                appliesToFinalRoom = rule.appliesToFinalRoom,
                allowedCategories = CloneCategories(rule.allowedCategories)
            };
        }

        return clone;
    }

    static RoomCategory[] CloneCategories(RoomCategory[] source)
    {
        if (source == null)
            return new RoomCategory[0];

        RoomCategory[] clone = new RoomCategory[source.Length];
        Array.Copy(source, clone, source.Length);
        return clone;
    }

    void OnValidate()
    {
        startingRoomCount = Mathf.Max(1, startingRoomCount);
        maxGeneratedRooms = Mathf.Max(0, maxGeneratedRooms);

        minimumBranchCount = Mathf.Max(1, minimumBranchCount);
        maximumBranchCount = Mathf.Max(minimumBranchCount, maximumBranchCount);
        minimumRoomsPerBranch = Mathf.Max(1, minimumRoomsPerBranch);
        maximumRoomsPerBranch = Mathf.Max(
            minimumRoomsPerBranch,
            maximumRoomsPerBranch);
        branchGenerationAttempts = Mathf.Max(1, branchGenerationAttempts);
        adjacentBranchConnectionChance =
            Mathf.Clamp01(adjacentBranchConnectionChance);
        maximumAdjacentBranchConnections =
            Mathf.Max(0, maximumAdjacentBranchConnections);
        maximumAdjacentBranchConnectionDoorDistance =
            Mathf.Max(0.1f, maximumAdjacentBranchConnectionDoorDistance);
        fullMapGenerationAttempts = Mathf.Max(1, fullMapGenerationAttempts);
        roomsToKeep = Mathf.Max(0, roomsToKeep);

        roomBoundsInset = Mathf.Max(0f, roomBoundsInset);
        placementAttempts = Mathf.Max(1, placementAttempts);

        doorwayClearanceWidth = Mathf.Max(0.1f, doorwayClearanceWidth);
        doorwayClearanceHeight = Mathf.Max(0.1f, doorwayClearanceHeight);
        doorwayClearanceDepth = Mathf.Max(0.1f, doorwayClearanceDepth);
        doorwayFloorBlockerTolerance = Mathf.Max(0f, doorwayFloorBlockerTolerance);

        navMeshLinkWidth = Mathf.Max(0.1f, navMeshLinkWidth);
        navMeshLinkWorldHeight = Mathf.Max(0f, navMeshLinkWorldHeight);
        navMeshLinkHalfLength = Mathf.Max(0.1f, navMeshLinkHalfLength);
        generationDebugMarkerSize = Mathf.Max(0.1f, generationDebugMarkerSize);
        generationDebugLabelHeight = Mathf.Max(0f, generationDebugLabelHeight);

        ValidateProgressionRules();
    }

    void ValidateProgressionRules()
    {
        if (progressionRules == null)
            return;

        for (int i = 0; i < progressionRules.Length; i++)
        {
            RoomProgressionRule rule = progressionRules[i];
            if (rule == null)
                continue;

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
}
