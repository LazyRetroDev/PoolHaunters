using System.Collections.Generic;
using NUnit.Framework;

public class RunEnemySelectionTests
{
    [TestCase(0, 1, 2)]
    [TestCase(1, 1, 2)]
    [TestCase(2, 1, 2)]
    [TestCase(3, 2, 3)]
    [TestCase(4, 2, 3)]
    [TestCase(5, 3, 4)]
    [TestCase(6, 4, 5)]
    [TestCase(14, 12, 13)]
    [TestCase(15, 13, 14)]
    [TestCase(16, 13, 14)]
    [TestCase(100, 13, 14)]
    public void CountRangeMatchesProgression(
        int phase,
        int expectedMinimum,
        int expectedMaximum)
    {
        RunEnemySelection.GetCountRange(
            phase,
            out int minimum,
            out int maximum);

        Assert.AreEqual(expectedMinimum, minimum);
        Assert.AreEqual(expectedMaximum, maximum);
    }

    [Test]
    public void OpeningPhasesOnlyUseAllowedCompositions()
    {
        List<RunEnemyCandidate> candidates = CreateFullRoster();
        bool sawSoloHard = false;
        bool sawEasyMediumPair = false;

        for (int phase = 1; phase <= 2; phase++)
        {
            for (int seed = 0; seed < 1000; seed++)
            {
                List<int> plan = new List<int>();
                Assert.IsTrue(RunEnemySelection.TryBuildPlan(
                    candidates,
                    phase,
                    seed,
                    plan));
                Assert.IsTrue(RunEnemySelection.IsPlanValid(
                    candidates,
                    phase,
                    plan));

                if (plan.Count == 1)
                {
                    sawSoloHard = true;
                    RunEnemyCandidate selected = Find(candidates, plan[0]);
                    Assert.AreEqual(
                        RunEnemyDifficulty.Hard,
                        selected.Difficulty);
                    Assert.IsFalse(selected.RequiresCompanion);
                }
                else
                {
                    sawEasyMediumPair = true;
                    Assert.AreEqual(2, plan.Count);
                    Assert.AreEqual(1, CountDifficulty(
                        candidates,
                        plan,
                        RunEnemyDifficulty.Easy));
                    Assert.AreEqual(1, CountDifficulty(
                        candidates,
                        plan,
                        RunEnemyDifficulty.Medium));
                }
            }
        }

        Assert.IsTrue(sawSoloHard);
        Assert.IsTrue(sawEasyMediumPair);
    }

    [Test]
    public void MiddlePhasesNeverSelectTwoHardEnemies()
    {
        List<RunEnemyCandidate> candidates = CreateFullRoster();

        for (int phase = 3; phase <= 4; phase++)
        {
            for (int seed = 0; seed < 1000; seed++)
            {
                List<int> plan = new List<int>();
                Assert.IsTrue(RunEnemySelection.TryBuildPlan(
                    candidates,
                    phase,
                    seed,
                    plan));
                Assert.That(plan.Count, Is.InRange(2, 3));
                Assert.LessOrEqual(
                    CountDifficulty(
                        candidates,
                        plan,
                        RunEnemyDifficulty.Hard),
                    1);
            }
        }
    }

    [Test]
    public void UnlimitedPhasesAllowAnyDifficultyAndStopGrowingAtFifteen()
    {
        List<RunEnemyCandidate> hardOnly = new List<RunEnemyCandidate>
        {
            new RunEnemyCandidate(0, RunEnemyDifficulty.Hard, false, 1f),
            new RunEnemyCandidate(1, RunEnemyDifficulty.Hard, false, 1f)
        };

        foreach (int phase in new[] { 5, 6, 15, 16, 100 })
        {
            RunEnemySelection.GetCountRange(
                phase,
                out int minimum,
                out int maximum);

            for (int seed = 0; seed < 250; seed++)
            {
                List<int> plan = new List<int>();
                Assert.IsTrue(RunEnemySelection.TryBuildPlan(
                    hardOnly,
                    phase,
                    seed,
                    plan));
                Assert.That(plan.Count, Is.InRange(minimum, maximum));
            }
        }
    }

    [Test]
    public void SupportEnemyCannotBecomeAOneEnemyPlan()
    {
        List<RunEnemyCandidate> supportOnly = new List<RunEnemyCandidate>
        {
            new RunEnemyCandidate(0, RunEnemyDifficulty.Easy, true, 1f)
        };
        List<int> plan = new List<int>();

        Assert.IsFalse(RunEnemySelection.TryBuildPlan(
            supportOnly,
            1,
            1234,
            plan));
        Assert.IsEmpty(plan);
    }

    [Test]
    public void SamePhaseAndSeedProduceSameOrderedPlan()
    {
        List<RunEnemyCandidate> candidates = CreateFullRoster();
        List<int> first = new List<int>();
        List<int> second = new List<int>();

        Assert.IsTrue(RunEnemySelection.TryBuildPlan(
            candidates,
            15,
            987654,
            first));
        Assert.IsTrue(RunEnemySelection.TryBuildPlan(
            candidates,
            15,
            987654,
            second));

        CollectionAssert.AreEqual(first, second);
    }

    [Test]
    public void EncounterPressureIncreasesWithQuietTimeAndIsCapped()
    {
        double early = EncounterDirectorPolicy.CalculateActivationChance(
            0.15d, 5d, 15d, 60d, false, 0.2d);
        double late = EncounterDirectorPolicy.CalculateActivationChance(
            0.15d, 45d, 15d, 60d, false, 0.2d);
        double guaranteed = EncounterDirectorPolicy.CalculateActivationChance(
            0.15d, 60d, 15d, 60d, true, 0.2d);

        Assert.AreEqual(0.15d, early, 0.0001d);
        Assert.Greater(late, early);
        Assert.AreEqual(1d, guaranteed, 0.0001d);
    }

    [Test]
    public void IsolatedPlayerRaisesEncounterPressure()
    {
        double grouped = EncounterDirectorPolicy.CalculateActivationChance(
            0.2d, 20d, 15d, 60d, false, 0.15d);
        double isolated = EncounterDirectorPolicy.CalculateActivationChance(
            0.2d, 20d, 15d, 60d, true, 0.15d);

        Assert.Greater(isolated, grouped);
    }

    [Test]
    public void ActivationPolicyHonorsCapsAndCooldowns()
    {
        Assert.IsTrue(EncounterDirectorPolicy.CanAttemptActivation(
            2, 0, 0, 2, 1, 10d, 5d, 30d, 20d));
        Assert.IsFalse(EncounterDirectorPolicy.CanAttemptActivation(
            2, 2, 0, 2, 1, 10d, 5d, 30d, 20d));
        Assert.IsFalse(EncounterDirectorPolicy.CanAttemptActivation(
            2, 0, 1, 2, 1, 10d, 5d, 30d, 20d));
        Assert.IsFalse(EncounterDirectorPolicy.CanAttemptActivation(
            2, 0, 0, 2, 1, 2d, 5d, 30d, 20d));
        Assert.IsFalse(EncounterDirectorPolicy.CanAttemptActivation(
            2, 0, 0, 2, 1, 10d, 5d, 5d, 20d));
    }

    static List<RunEnemyCandidate> CreateFullRoster()
    {
        return new List<RunEnemyCandidate>
        {
            new RunEnemyCandidate(0, RunEnemyDifficulty.Easy, true, 1f),
            new RunEnemyCandidate(1, RunEnemyDifficulty.Easy, true, 1f),
            new RunEnemyCandidate(2, RunEnemyDifficulty.Medium, true, 1f),
            new RunEnemyCandidate(3, RunEnemyDifficulty.Medium, false, 1f),
            new RunEnemyCandidate(4, RunEnemyDifficulty.Medium, false, 1f),
            new RunEnemyCandidate(5, RunEnemyDifficulty.Medium, false, 1f),
            new RunEnemyCandidate(6, RunEnemyDifficulty.Hard, false, 1f),
            new RunEnemyCandidate(7, RunEnemyDifficulty.Hard, false, 1f)
        };
    }

    static int CountDifficulty(
        IReadOnlyList<RunEnemyCandidate> candidates,
        IReadOnlyList<int> plan,
        RunEnemyDifficulty difficulty)
    {
        int count = 0;
        for (int i = 0; i < plan.Count; i++)
        {
            if (Find(candidates, plan[i]).Difficulty == difficulty)
                count++;
        }

        return count;
    }

    static RunEnemyCandidate Find(
        IReadOnlyList<RunEnemyCandidate> candidates,
        int sourceIndex)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].SourceIndex == sourceIndex)
                return candidates[i];
        }

        Assert.Fail($"Candidate {sourceIndex} was not found.");
        return new RunEnemyCandidate();
    }
}
