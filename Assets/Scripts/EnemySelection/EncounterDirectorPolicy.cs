using System;

public static class EncounterDirectorPolicy
{
    public static bool CanAttemptActivation(
        int availableCount,
        int activeCount,
        int encounterCount,
        int maximumActive,
        int maximumEncounters,
        double secondsSinceLastSpawn,
        double minimumSpawnInterval,
        double secondsSinceLastEncounterEnded,
        double encounterCooldown)
    {
        if (availableCount <= 0 || activeCount >= Math.Max(1, maximumActive))
            return false;
        if (encounterCount >= Math.Max(1, maximumEncounters))
            return false;
        if (secondsSinceLastSpawn < Math.Max(0d, minimumSpawnInterval))
            return false;

        return secondsSinceLastEncounterEnded >= Math.Max(0d, encounterCooldown);
    }

    public static double CalculateActivationChance(
        double baseChance,
        double quietSeconds,
        double quietRampStart,
        double guaranteedAt,
        bool hasIsolatedPlayer,
        double isolatedPlayerBonus)
    {
        double clampedBase = Clamp01(baseChance);
        double start = Math.Max(0d, quietRampStart);
        double end = Math.Max(start, guaranteedAt);
        double quietPressure;

        if (end <= start)
        {
            quietPressure = quietSeconds >= end ? 1d : 0d;
        }
        else
        {
            quietPressure = Clamp01((quietSeconds - start) / (end - start));
        }

        double chance = clampedBase + (1d - clampedBase) * quietPressure;
        if (hasIsolatedPlayer)
            chance += Math.Max(0d, isolatedPlayerBonus);

        return Clamp01(chance);
    }

    static double Clamp01(double value)
    {
        if (value <= 0d)
            return 0d;
        if (value >= 1d)
            return 1d;

        return value;
    }
}
