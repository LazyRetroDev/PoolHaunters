using System;
using UnityEngine;

public static class NoiseEvent
{
    public static event Action<Vector3, float, GameObject> OnNoiseEmitted;

    public static void Emit(Vector3 position, float radius, GameObject source)
    {
        if (radius <= 0f) return;
        OnNoiseEmitted?.Invoke(position, radius, source);
    }
}
