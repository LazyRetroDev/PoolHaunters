using System;
using UnityEngine;

[Flags]
public enum RoomResourceCategory
{
    None = 0,
    Pickup = 1 << 0,
    WaterSource = 1 << 1,
    All = Pickup | WaterSource
}

[DisallowMultipleComponent]
public class RoomResourceSpawnPoint : MonoBehaviour
{
    [Tooltip("Only resources in these categories can use this point.")]
    public RoomResourceCategory allowedCategories = RoomResourceCategory.All;

    [Range(0f, 1f)]
    [Tooltip("Chance that this point receives a resource when its room is generated.")]
    public float spawnChance = 0.65f;

    [Tooltip("Optional child transform used as the exact placement pose.")]
    public Transform placement;

    public Vector3 localPositionOffset;
    public Vector3 localRotationOffset;

    public bool Allows(RoomResourceCategory category)
    {
        return category != RoomResourceCategory.None &&
            (allowedCategories & category) != RoomResourceCategory.None;
    }

    public void GetSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        Transform source = placement != null ? placement : transform;
        position = source.TransformPoint(localPositionOffset);
        rotation = source.rotation * Quaternion.Euler(localRotationOffset);
    }

    void OnDrawGizmos()
    {
        GetSpawnPose(out Vector3 position, out Quaternion rotation);

        Gizmos.color = allowedCategories == RoomResourceCategory.WaterSource
            ? Color.cyan
            : allowedCategories == RoomResourceCategory.Pickup
                ? Color.yellow
                : Color.green;

        Gizmos.DrawWireCube(position, Vector3.one * 0.35f);
        Gizmos.DrawRay(position, rotation * Vector3.forward * 0.5f);
    }
}
