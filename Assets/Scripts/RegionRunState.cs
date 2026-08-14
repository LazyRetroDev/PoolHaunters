using UnityEngine;

public enum RunLaunchMode
{
    SinglePlayer,
    MultiplayerHost,
    MultiplayerClient
}

public enum RunNetworkMode
{
    Direct,
    Relay
}

public enum RunDifficulty
{
    Easy,
    Medium,
    Hard,
    Gradual
}

public static class RegionRunState
{
    public static bool HasSelectedRegion { get; private set; }
    public static string RegionName { get; private set; }
    public static string SceneName { get; private set; }
    public static string PreviousSceneName { get; private set; }
    public static int RunSeed { get; private set; }
    public static int PhaseNumber { get; private set; } = 1;
    public static RunLaunchMode LaunchMode { get; private set; } =
        RunLaunchMode.SinglePlayer;
    public static RunNetworkMode NetworkMode { get; private set; } =
        RunNetworkMode.Direct;
    public static string ConnectionAddress { get; private set; } = "127.0.0.1";
    public static ushort ConnectionPort { get; private set; } = 7777;
    public static string RelayJoinCode { get; private set; } = string.Empty;
    public static string RelayConnectionType { get; private set; } = "dtls";
    public static int RelayMaxConnections { get; private set; } = 3;
    public static string PlayerName { get; private set; } = "Player";
    public static RunDifficulty DifficultyMode { get; private set; } =
        RunDifficulty.Gradual;
    public static RunDifficulty Difficulty { get; private set; } =
        RunDifficulty.Gradual;

    public static bool IsSinglePlayer
    {
        get { return LaunchMode == RunLaunchMode.SinglePlayer; }
    }

    public static bool IsMultiplayer
    {
        get { return LaunchMode != RunLaunchMode.SinglePlayer; }
    }

    public static bool IsHost
    {
        get { return LaunchMode == RunLaunchMode.MultiplayerHost; }
    }

    public static bool IsClient
    {
        get { return LaunchMode == RunLaunchMode.MultiplayerClient; }
    }

    public static bool UsesRelay
    {
        get { return IsMultiplayer && NetworkMode == RunNetworkMode.Relay; }
    }

    public static void SelectRegion(
        string regionName,
        string sceneName,
        int runSeed)
    {
        SelectSinglePlayerRegion(regionName, sceneName, runSeed);
    }

    public static void SelectSinglePlayerRegion(
        string regionName,
        string sceneName,
        int runSeed,
        RunDifficulty difficulty = RunDifficulty.Gradual)
    {
        SelectRegion(
            regionName,
            sceneName,
            runSeed,
            RunLaunchMode.SinglePlayer,
            RunNetworkMode.Direct,
            "127.0.0.1",
            7777,
            string.Empty,
            "dtls",
            3,
            1,
            string.Empty,
            difficulty);
    }

    public static void SelectMultiplayerHostRegion(
        string regionName,
        string sceneName,
        int runSeed,
        ushort port = 7777,
        RunDifficulty difficulty = RunDifficulty.Gradual)
    {
        SelectRegion(
            regionName,
            sceneName,
            runSeed,
            RunLaunchMode.MultiplayerHost,
            RunNetworkMode.Direct,
            "127.0.0.1",
            port,
            string.Empty,
            "dtls",
            3,
            1,
            string.Empty,
            difficulty);
    }

    public static void SelectMultiplayerClientRegion(
        string regionName,
        string sceneName,
        int runSeed,
        string address,
        ushort port = 7777,
        RunDifficulty difficulty = RunDifficulty.Gradual)
    {
        SelectRegion(
            regionName,
            sceneName,
            runSeed,
            RunLaunchMode.MultiplayerClient,
            RunNetworkMode.Direct,
            address,
            port,
            string.Empty,
            "dtls",
            3,
            1,
            string.Empty,
            difficulty);
    }

    public static void SelectRelayHostRegion(
        string regionName,
        string sceneName,
        int runSeed,
        int maxConnections = 3,
        string connectionType = "dtls",
        RunDifficulty difficulty = RunDifficulty.Gradual)
    {
        SelectRegion(
            regionName,
            sceneName,
            runSeed,
            RunLaunchMode.MultiplayerHost,
            RunNetworkMode.Relay,
            "127.0.0.1",
            7777,
            string.Empty,
            connectionType,
            maxConnections,
            1,
            string.Empty,
            difficulty);
    }

    public static void SelectRelayClientRegion(
        string regionName,
        string sceneName,
        int runSeed,
        string joinCode,
        string connectionType = "dtls",
        RunDifficulty difficulty = RunDifficulty.Gradual)
    {
        SelectRegion(
            regionName,
            sceneName,
            runSeed,
            RunLaunchMode.MultiplayerClient,
            RunNetworkMode.Relay,
            "127.0.0.1",
            7777,
            joinCode,
            connectionType,
            1,
            1,
            string.Empty,
            difficulty);
    }

    public static void SelectNextPhaseRegion(
        string regionName,
        string sceneName,
        int runSeed)
    {
        string previousSceneName = SceneName;
        int nextPhaseNumber = Mathf.Max(1, PhaseNumber + 1);

        SelectRegion(
            regionName,
            sceneName,
            runSeed,
            LaunchMode,
            NetworkMode,
            ConnectionAddress,
            ConnectionPort,
            RelayJoinCode,
            RelayConnectionType,
            RelayMaxConnections,
            nextPhaseNumber,
            previousSceneName,
            DifficultyMode);
    }

    static void SelectRegion(
        string regionName,
        string sceneName,
        int runSeed,
        RunLaunchMode launchMode,
        RunNetworkMode networkMode,
        string connectionAddress,
        ushort connectionPort,
        string relayJoinCode,
        string relayConnectionType,
        int relayMaxConnections,
        int phaseNumber,
        string previousSceneName,
        RunDifficulty difficulty)
    {
        HasSelectedRegion = true;
        RegionName = string.IsNullOrWhiteSpace(regionName) ? sceneName : regionName;
        SceneName = sceneName;
        PreviousSceneName = previousSceneName;
        RunSeed = runSeed;
        PhaseNumber = Mathf.Max(1, phaseNumber);
        LaunchMode = launchMode;
        NetworkMode = networkMode;
        ConnectionAddress = string.IsNullOrWhiteSpace(connectionAddress)
            ? "127.0.0.1"
            : connectionAddress;
        ConnectionPort = connectionPort;
        RelayJoinCode = SanitizeRelayJoinCode(relayJoinCode);
        RelayConnectionType = SanitizeRelayConnectionType(relayConnectionType);
        RelayMaxConnections = Mathf.Max(1, relayMaxConnections);
        DifficultyMode = difficulty;
        Difficulty = ResolveEffectiveDifficulty(difficulty, PhaseNumber);

        Debug.Log(
            $"Selected phase {PhaseNumber} region '{RegionName}' in scene '{SceneName}' with seed {RunSeed}. Launch mode: {LaunchMode}. Network mode: {NetworkMode}. Difficulty mode: {DifficultyMode}. Effective difficulty: {Difficulty}.");
    }

    public static void SetRelayJoinCode(string joinCode)
    {
        RelayJoinCode = SanitizeRelayJoinCode(joinCode);
    }

    public static void SetPlayerName(string name)
    {
        PlayerName = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
    }

    public static void SetDifficulty(RunDifficulty difficulty)
    {
        DifficultyMode = difficulty;
        Difficulty = ResolveEffectiveDifficulty(difficulty, PhaseNumber);
    }

    public static void Clear()
    {
        HasSelectedRegion = false;
        RegionName = string.Empty;
        SceneName = string.Empty;
        PreviousSceneName = string.Empty;
        RunSeed = 0;
        PhaseNumber = 1;
        LaunchMode = RunLaunchMode.SinglePlayer;
        NetworkMode = RunNetworkMode.Direct;
        ConnectionAddress = "127.0.0.1";
        ConnectionPort = 7777;
        RelayJoinCode = string.Empty;
        RelayConnectionType = "dtls";
        RelayMaxConnections = 3;
        PlayerName = "Player";
        DifficultyMode = RunDifficulty.Gradual;
        Difficulty = RunDifficulty.Gradual;
    }

    static RunDifficulty ResolveEffectiveDifficulty(
        RunDifficulty difficulty,
        int phaseNumber)
    {
        if (difficulty != RunDifficulty.Gradual)
            return difficulty;

        int phase = Mathf.Max(1, phaseNumber);
        if (phase <= 2)
            return RunDifficulty.Easy;
        if (phase <= 4)
            return RunDifficulty.Medium;

        return RunDifficulty.Hard;
    }

    static string SanitizeRelayJoinCode(string joinCode)
    {
        return string.IsNullOrWhiteSpace(joinCode)
            ? string.Empty
            : joinCode.Trim().ToUpperInvariant();
    }

    static string SanitizeRelayConnectionType(string connectionType)
    {
        if (string.IsNullOrWhiteSpace(connectionType))
            return "dtls";

        string sanitized = connectionType.Trim().ToLowerInvariant();
        switch (sanitized)
        {
            case "udp":
            case "dtls":
            case "wss":
                return sanitized;
            default:
                Debug.LogWarning(
                    $"Unsupported Relay connection type '{connectionType}'. Use udp, dtls or wss. Falling back to dtls.");
                return "dtls";
        }
    }
}
