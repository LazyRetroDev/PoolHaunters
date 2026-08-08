using UnityEngine;

[CreateAssetMenu(
    fileName = "RunPhaseProfile",
    menuName = "Pool Haunters/Run/Phase Profile")]
public class RunPhaseProfile : ScriptableObject
{
    [Header("Map Layout")]
    [Min(1)] public int minimumBranchCount = 4;
    [Min(1)] public int maximumBranchCount = 6;
    [Min(1)] public int minimumRoomsPerBranch = 6;
    [Min(1)] public int maximumRoomsPerBranch = 8;

    [Header("Pool Rooms")]
    public bool requirePoolRoomsInFullMap = true;
    [Min(0)] public int minimumPoolRoomsInMap = 1;

    [Header("Mandatory Pools")]
    public bool randomizeMandatoryPools = true;
    [Min(1)] public int guaranteedMandatoryPools = 1;
    [Min(0)] public int guaranteedOptionalPools = 1;
    [Range(0f, 1f)] public float extraPoolMandatoryChance = 0.5f;

    void OnValidate()
    {
        minimumBranchCount = Mathf.Max(1, minimumBranchCount);
        maximumBranchCount = Mathf.Max(minimumBranchCount, maximumBranchCount);
        minimumRoomsPerBranch = Mathf.Max(1, minimumRoomsPerBranch);
        maximumRoomsPerBranch = Mathf.Max(
            minimumRoomsPerBranch,
            maximumRoomsPerBranch);

        minimumPoolRoomsInMap = Mathf.Max(0, minimumPoolRoomsInMap);
        guaranteedMandatoryPools = Mathf.Max(1, guaranteedMandatoryPools);
        guaranteedOptionalPools = Mathf.Max(0, guaranteedOptionalPools);
        extraPoolMandatoryChance = Mathf.Clamp01(extraPoolMandatoryChance);
    }
}
