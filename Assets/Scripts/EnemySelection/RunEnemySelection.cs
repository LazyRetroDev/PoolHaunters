using System;
using System.Collections.Generic;

public struct RunEnemyCandidate
{
    public int SourceIndex { get; }
    public RunEnemyDifficulty Difficulty { get; }
    public bool RequiresCompanion { get; }
    public float Weight { get; }

    public RunEnemyCandidate(
        int sourceIndex,
        RunEnemyDifficulty difficulty,
        bool requiresCompanion,
        float weight)
    {
        SourceIndex = sourceIndex;
        Difficulty = difficulty;
        RequiresCompanion = requiresCompanion;
        Weight = weight;
    }
}

public static class RunEnemySelection
{
    public const int GrowthStartPhase = 5;
    public const int GrowthStopPhase = 15;

    public static void GetCountRange(
        int phaseNumber,
        out int minimumCount,
        out int maximumCount)
    {
        int phase = Math.Max(1, phaseNumber);

        if (phase <= 2)
        {
            minimumCount = 1;
            maximumCount = 2;
            return;
        }

        if (phase <= 4)
        {
            minimumCount = 2;
            maximumCount = 3;
            return;
        }

        int cappedPhase = Math.Min(phase, GrowthStopPhase);
        int growth = cappedPhase - GrowthStartPhase;
        minimumCount = 3 + growth;
        maximumCount = 4 + growth;
    }

    public static bool TryBuildPlan(
        IReadOnlyList<RunEnemyCandidate> candidates,
        int phaseNumber,
        int seed,
        List<int> selectedSourceIndices)
    {
        if (selectedSourceIndices == null)
            throw new ArgumentNullException(nameof(selectedSourceIndices));

        selectedSourceIndices.Clear();
        List<RunEnemyCandidate> eligible = CollectEligibleCandidates(candidates);
        if (eligible.Count == 0)
            return false;

        int phase = Math.Max(1, phaseNumber);
        Random random = new Random(seed);

        bool built;
        if (phase <= 2)
        {
            built = BuildOpeningPhasePlan(
                eligible,
                random,
                selectedSourceIndices);
        }
        else if (phase <= 4)
        {
            built = BuildMiddlePhasePlan(
                eligible,
                random,
                selectedSourceIndices);
        }
        else
        {
            built = BuildUnlimitedPhasePlan(
                eligible,
                phase,
                random,
                selectedSourceIndices);
        }

        return built &&
            IsPlanValid(eligible, phase, selectedSourceIndices);
    }

    public static bool IsPlanValid(
        IReadOnlyList<RunEnemyCandidate> candidates,
        int phaseNumber,
        IReadOnlyList<int> selectedSourceIndices)
    {
        if (candidates == null || selectedSourceIndices == null)
            return false;

        int phase = Math.Max(1, phaseNumber);
        GetCountRange(phase, out int minimumCount, out int maximumCount);
        int count = selectedSourceIndices.Count;
        if (count < minimumCount || count > maximumCount)
            return false;

        int easyCount = 0;
        int mediumCount = 0;
        int hardCount = 0;

        for (int i = 0; i < selectedSourceIndices.Count; i++)
        {
            if (!TryFindCandidate(
                candidates,
                selectedSourceIndices[i],
                out RunEnemyCandidate candidate))
            {
                return false;
            }

            if (count == 1 && candidate.RequiresCompanion)
                return false;

            switch (candidate.Difficulty)
            {
                case RunEnemyDifficulty.Easy:
                    easyCount++;
                    break;
                case RunEnemyDifficulty.Medium:
                    mediumCount++;
                    break;
                case RunEnemyDifficulty.Hard:
                    hardCount++;
                    break;
            }
        }

        if (phase <= 2)
        {
            return count == 1
                ? hardCount == 1
                : count == 2 && easyCount == 1 && mediumCount == 1;
        }

        if (phase <= 4)
            return hardCount <= 1;

        return true;
    }

    static bool BuildOpeningPhasePlan(
        List<RunEnemyCandidate> eligible,
        Random random,
        List<int> result)
    {
        List<RunEnemyCandidate> soloHard = FilterByDifficulty(
            eligible,
            RunEnemyDifficulty.Hard,
            requireNoCompanion: true);
        List<RunEnemyCandidate> easy = FilterByDifficulty(
            eligible,
            RunEnemyDifficulty.Easy,
            requireNoCompanion: false);
        List<RunEnemyCandidate> medium = FilterByDifficulty(
            eligible,
            RunEnemyDifficulty.Medium,
            requireNoCompanion: false);

        bool canUseSoloHard = soloHard.Count > 0;
        bool canUsePair = easy.Count > 0 && medium.Count > 0;
        if (!canUseSoloHard && !canUsePair)
            return false;

        bool useSoloHard = canUseSoloHard &&
            (!canUsePair || random.Next(0, 2) == 0);

        if (useSoloHard)
        {
            result.Add(ChooseWeighted(soloHard, random).SourceIndex);
            return true;
        }

        result.Add(ChooseWeighted(easy, random).SourceIndex);
        result.Add(ChooseWeighted(medium, random).SourceIndex);
        return true;
    }

    static bool BuildMiddlePhasePlan(
        List<RunEnemyCandidate> eligible,
        Random random,
        List<int> result)
    {
        int targetCount = random.Next(2, 4);
        List<RunEnemyCandidate> unused =
            new List<RunEnemyCandidate>(eligible);
        int hardCount = 0;

        while (result.Count < targetCount)
        {
            List<RunEnemyCandidate> compatible = FilterForHardLimit(
                unused,
                hardCount);

            if (compatible.Count == 0)
            {
                compatible = FilterForHardLimit(eligible, hardCount);
                if (compatible.Count == 0)
                    return false;
            }

            RunEnemyCandidate selected = ChooseWeighted(compatible, random);
            result.Add(selected.SourceIndex);
            if (selected.Difficulty == RunEnemyDifficulty.Hard)
                hardCount++;

            RemoveBySourceIndex(unused, selected.SourceIndex);
        }

        return true;
    }

    static bool BuildUnlimitedPhasePlan(
        List<RunEnemyCandidate> eligible,
        int phase,
        Random random,
        List<int> result)
    {
        GetCountRange(phase, out int minimumCount, out int maximumCount);
        int targetCount = random.Next(minimumCount, maximumCount + 1);
        List<RunEnemyCandidate> unused =
            new List<RunEnemyCandidate>(eligible);

        while (result.Count < targetCount)
        {
            if (unused.Count == 0)
                unused.AddRange(eligible);

            RunEnemyCandidate selected = ChooseWeighted(unused, random);
            result.Add(selected.SourceIndex);
            RemoveBySourceIndex(unused, selected.SourceIndex);
        }

        return true;
    }

    static List<RunEnemyCandidate> CollectEligibleCandidates(
        IReadOnlyList<RunEnemyCandidate> candidates)
    {
        List<RunEnemyCandidate> eligible = new List<RunEnemyCandidate>();
        if (candidates == null)
            return eligible;

        HashSet<int> sourceIndices = new HashSet<int>();
        for (int i = 0; i < candidates.Count; i++)
        {
            RunEnemyCandidate candidate = candidates[i];
            if (candidate.Weight <= 0f ||
                float.IsNaN(candidate.Weight) ||
                float.IsInfinity(candidate.Weight) ||
                !sourceIndices.Add(candidate.SourceIndex))
            {
                continue;
            }

            eligible.Add(candidate);
        }

        return eligible;
    }

    static List<RunEnemyCandidate> FilterByDifficulty(
        List<RunEnemyCandidate> candidates,
        RunEnemyDifficulty difficulty,
        bool requireNoCompanion)
    {
        List<RunEnemyCandidate> filtered = new List<RunEnemyCandidate>();
        for (int i = 0; i < candidates.Count; i++)
        {
            RunEnemyCandidate candidate = candidates[i];
            if (candidate.Difficulty != difficulty)
                continue;
            if (requireNoCompanion && candidate.RequiresCompanion)
                continue;

            filtered.Add(candidate);
        }

        return filtered;
    }

    static List<RunEnemyCandidate> FilterForHardLimit(
        List<RunEnemyCandidate> candidates,
        int currentHardCount)
    {
        List<RunEnemyCandidate> filtered = new List<RunEnemyCandidate>();
        for (int i = 0; i < candidates.Count; i++)
        {
            RunEnemyCandidate candidate = candidates[i];
            if (currentHardCount >= 1 &&
                candidate.Difficulty == RunEnemyDifficulty.Hard)
            {
                continue;
            }

            filtered.Add(candidate);
        }

        return filtered;
    }

    static RunEnemyCandidate ChooseWeighted(
        List<RunEnemyCandidate> candidates,
        Random random)
    {
        double totalWeight = 0d;
        for (int i = 0; i < candidates.Count; i++)
            totalWeight += candidates[i].Weight;

        double roll = random.NextDouble() * totalWeight;
        for (int i = 0; i < candidates.Count; i++)
        {
            roll -= candidates[i].Weight;
            if (roll <= 0d)
                return candidates[i];
        }

        return candidates[candidates.Count - 1];
    }

    static void RemoveBySourceIndex(
        List<RunEnemyCandidate> candidates,
        int sourceIndex)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].SourceIndex != sourceIndex)
                continue;

            candidates.RemoveAt(i);
            return;
        }
    }

    static bool TryFindCandidate(
        IReadOnlyList<RunEnemyCandidate> candidates,
        int sourceIndex,
        out RunEnemyCandidate selected)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].SourceIndex != sourceIndex)
                continue;

            selected = candidates[i];
            return true;
        }

        selected = new RunEnemyCandidate();
        return false;
    }
}
