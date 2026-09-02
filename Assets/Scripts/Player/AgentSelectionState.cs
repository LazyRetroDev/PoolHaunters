using UnityEngine;

public enum PlayerAgentType
{
    JennyPie,
    Sylvian,
    SecretAgent,
    Louise
}

public static class AgentSelectionState
{
    const string SelectedAgentKey = "PoolHaunters.SelectedAgent";

    public static PlayerAgentType SelectedAgent { get; private set; } =
        PlayerAgentType.JennyPie;

    static AgentSelectionState()
    {
        Load();
    }

    public static void Select(PlayerAgentType agent)
    {
        SelectedAgent = agent;
        PlayerPrefs.SetInt(SelectedAgentKey, (int)agent);
        PlayerPrefs.Save();
    }

    public static string GetDisplayName(PlayerAgentType agent)
    {
        switch (agent)
        {
            case PlayerAgentType.JennyPie:
                return "Jenny Pie";
            case PlayerAgentType.Sylvian:
                return "Sylvian";
            case PlayerAgentType.SecretAgent:
                return "Secret Agent";
            case PlayerAgentType.Louise:
                return "Louise";
            default:
                return agent.ToString();
        }
    }

    public static string GetRoleName(PlayerAgentType agent)
    {
        switch (agent)
        {
            case PlayerAgentType.JennyPie:
                return "Mop Specialist";
            case PlayerAgentType.Sylvian:
                return "Field Agent";
            case PlayerAgentType.SecretAgent:
                return "Field Agent";
            case PlayerAgentType.Louise:
                return "Field Agent";
            default:
                return "Agent";
        }
    }

    public static string GetDescription(PlayerAgentType agent)
    {
        switch (agent)
        {
            case PlayerAgentType.JennyPie:
                return "Cleans with a wide mop sweep, spends water on mop actions, and uses a short-range splash to fight back.";
            case PlayerAgentType.Sylvian:
                return "Uses the standard water cannon and item slots. Unique abilities can be added later.";
            case PlayerAgentType.SecretAgent:
                return "Uses the standard water cannon and item slots. Unique abilities can be added later.";
            case PlayerAgentType.Louise:
                return "Uses the standard water cannon and item slots. Unique abilities can be added later.";
            default:
                return string.Empty;
        }
    }

    public static string GetLoadoutSummary(PlayerAgentType agent)
    {
        switch (agent)
        {
            case PlayerAgentType.JennyPie:
                return "Loadout: mop cleaner, mop dash, water splash. Water cannon disabled.";
            default:
                return "Loadout: water cannon and inventory items.";
        }
    }

    static void Load()
    {
        int saved = PlayerPrefs.GetInt(SelectedAgentKey, (int)SelectedAgent);
        if (System.Enum.IsDefined(typeof(PlayerAgentType), saved))
            SelectedAgent = (PlayerAgentType)saved;
    }
}
