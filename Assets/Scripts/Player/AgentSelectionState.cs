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

    static void Load()
    {
        int saved = PlayerPrefs.GetInt(SelectedAgentKey, (int)SelectedAgent);
        if (System.Enum.IsDefined(typeof(PlayerAgentType), saved))
            SelectedAgent = (PlayerAgentType)saved;
    }
}
