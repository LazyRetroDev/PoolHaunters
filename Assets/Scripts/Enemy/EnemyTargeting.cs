using Unity.Netcode;
using UnityEngine;

public static class EnemyTargeting
{
    public static bool TryFindClosestPlayer(
        Vector3 origin,
        out PlayerStatus status,
        out Transform target,
        float maxDistance = float.PositiveInfinity,
        bool requireCanAct = true)
    {
        status = null;
        target = null;

        PlayerStatus[] players =
            Object.FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);

        float maxSqrDistance = float.IsPositiveInfinity(maxDistance)
            ? float.PositiveInfinity
            : maxDistance * maxDistance;
        float bestSqrDistance = maxSqrDistance;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus candidate = players[i];
            if (!IsValidTarget(candidate, requireCanAct))
                continue;

            float sqrDistance =
                (candidate.transform.position - origin).sqrMagnitude;
            if (sqrDistance > bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            status = candidate;
            target = candidate.transform;
        }

        return status != null && target != null;
    }

    public static bool TryGetPlayerStatus(
        GameObject source,
        out PlayerStatus status,
        bool requireCanAct = true)
    {
        status = source != null
            ? source.GetComponentInParent<PlayerStatus>()
            : null;

        return IsValidTarget(status, requireCanAct);
    }

    public static bool IsValidTarget(
        PlayerStatus status,
        bool requireCanAct = true)
    {
        if (status == null || !status.gameObject.activeInHierarchy)
            return false;
        if (status.IsDead() || status.IsTransformed())
            return false;

        return !requireCanAct || status.CanAct();
    }
}

public static class EnemyAuthority
{
    public static bool CanRunGameplay()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return true;

        return networkManager.IsServer;
    }

    public static bool IsClientReplica()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null &&
            networkManager.IsListening &&
            !networkManager.IsServer;
    }
}

public static class EnemyPlayerEffects
{
    public static void SetThreatIntensity(
        ref PlayerMovement effectTarget,
        PlayerStatus status,
        Transform player,
        PlayerVignetteEffect fallback,
        float intensity)
    {
        PlayerMovement movement = ResolveEffectTarget(
            ref effectTarget,
            status,
            player);

        if (movement != null)
        {
            movement.SetLocalThreatIntensity(intensity);
            return;
        }

        if (fallback != null)
            fallback.SetThreatIntensity(intensity);
    }

    public static void ClearThreat(
        ref PlayerMovement effectTarget,
        PlayerVignetteEffect fallback,
        bool stopShake = false)
    {
        if (effectTarget != null)
        {
            effectTarget.ClearLocalThreatEffect(stopShake);
            effectTarget = null;
        }

        if (fallback == null)
            return;

        fallback.ClearThreatIntensity();
        if (stopShake)
            fallback.StopShake();
    }

    public static void Pulse(
        ref PlayerMovement effectTarget,
        PlayerStatus status,
        Transform player,
        PlayerVignetteEffect fallback,
        float intensity,
        float duration,
        float shakeAmplitude = 0f,
        float shakeFrequency = 0f,
        float shakeDuration = 0f)
    {
        PlayerMovement movement = ResolveEffectTarget(
            ref effectTarget,
            status,
            player);

        if (movement != null)
        {
            movement.PulseLocalThreatEffect(
                intensity,
                duration,
                shakeAmplitude,
                shakeFrequency,
                shakeDuration);
            return;
        }

        if (fallback == null)
            return;

        fallback.Pulse(intensity, duration);
        if (shakeAmplitude > 0f && shakeDuration > 0f)
            fallback.Shake(shakeAmplitude, shakeFrequency, shakeDuration);
    }

    public static void Shake(
        ref PlayerMovement effectTarget,
        PlayerStatus status,
        Transform player,
        PlayerVignetteEffect fallback,
        float amplitude,
        float frequency,
        float duration)
    {
        PlayerMovement movement = ResolveEffectTarget(
            ref effectTarget,
            status,
            player);

        if (movement != null)
        {
            movement.ShakeLocalThreatEffect(amplitude, frequency, duration);
            return;
        }

        if (fallback != null)
            fallback.Shake(amplitude, frequency, duration);
    }

    static PlayerMovement ResolveEffectTarget(
        ref PlayerMovement effectTarget,
        PlayerStatus status,
        Transform player)
    {
        PlayerMovement movement = ResolvePlayerMovement(status, player);
        if (effectTarget != null && effectTarget != movement)
            effectTarget.ClearLocalThreatEffect(true);

        effectTarget = movement;
        return movement;
    }

    static PlayerMovement ResolvePlayerMovement(
        PlayerStatus status,
        Transform player)
    {
        if (status != null)
        {
            PlayerMovement movement = status.GetComponent<PlayerMovement>();
            if (movement != null)
                return movement;
        }

        return player != null ? player.GetComponent<PlayerMovement>() : null;
    }
}
