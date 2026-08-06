using UnityEngine;

public static class PlayerCurrencyState
{
    const string GermsKey = "PoolHaunters.Currency.Germs";

    public static int Germs { get; private set; } = LoadGerms();
    public static int LastRunEarnedGerms { get; private set; }
    public static float LastRunPersonalCleaningPercent { get; private set; }
    public static float LastRunTeamCleaningPercent { get; private set; }
    public static float LastRunTimePercent { get; private set; }
    public static int LastRunKnockouts { get; private set; }
    public static int LastRunDeaths { get; private set; }
    public static bool LastRunWasTransformed { get; private set; }

    public static void AddGerms(int amount)
    {
        if (amount <= 0)
            return;

        Germs = Mathf.Max(0, Germs + amount);
        Save();
    }

    public static bool SpendGerms(int amount)
    {
        if (amount <= 0)
            return true;

        if (Germs < amount)
            return false;

        Germs -= amount;
        Save();
        return true;
    }

    public static void SetLastRunReward(
        int earnedGerms,
        float personalCleaningPercent,
        float teamCleaningPercent,
        float timePercent,
        int knockouts,
        int deaths,
        bool wasTransformed)
    {
        LastRunEarnedGerms = Mathf.Max(0, earnedGerms);
        LastRunPersonalCleaningPercent = Mathf.Clamp01(personalCleaningPercent);
        LastRunTeamCleaningPercent = Mathf.Clamp01(teamCleaningPercent);
        LastRunTimePercent = Mathf.Clamp01(timePercent);
        LastRunKnockouts = Mathf.Max(0, knockouts);
        LastRunDeaths = Mathf.Max(0, deaths);
        LastRunWasTransformed = wasTransformed;
    }

    public static void ResetGerms()
    {
        Germs = 0;
        Save();
    }

    static int LoadGerms()
    {
        return Mathf.Max(0, PlayerPrefs.GetInt(GermsKey, 0));
    }

    static void Save()
    {
        PlayerPrefs.SetInt(GermsKey, Germs);
        PlayerPrefs.Save();
    }
}
