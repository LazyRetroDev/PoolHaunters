using System;
using UnityEngine;

[Flags]
public enum RoomEnemyCategory
{
    None = 0,
    Common = 1 << 0,
    Stalker = 1 << 1,
    Heavy = 1 << 2,
    Special = 1 << 3,
    All = Common | Stalker | Heavy | Special
}

[DisallowMultipleComponent]
public class RoomEnemySpawnPoint : MonoBehaviour
{
    [Tooltip("Only enemies in these categories can use this point.")]
    public RoomEnemyCategory allowedCategories = RoomEnemyCategory.All;

    [Range(0f, 1f)]
    [Tooltip("Chance that this point receives an enemy when its room is generated.")]
    public float spawnChance = 0.35f;

    [Tooltip("Optional child transform used as the exact placement pose.")]
    public Transform placement;

    public Vector3 localPositionOffset;
    public Vector3 localRotationOffset;

    public void GetSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        Transform source = placement != null ? placement : transform;
        position = source.TransformPoint(localPositionOffset);
        rotation = source.rotation * Quaternion.Euler(localRotationOffset);
    }

    void OnDrawGizmos()
    {
        GetSpawnPose(out Vector3 position, out Quaternion rotation);

        Gizmos.color = allowedCategories == RoomEnemyCategory.Special
            ? Color.magenta
            : allowedCategories == RoomEnemyCategory.Heavy
                ? Color.red
                : allowedCategories == RoomEnemyCategory.Stalker
                    ? new Color(1f, 0.45f, 0f)
                    : Color.yellow;

        Gizmos.DrawWireSphere(position, 0.45f);
        Gizmos.DrawRay(position, rotation * Vector3.forward * 0.75f);
    }
}
