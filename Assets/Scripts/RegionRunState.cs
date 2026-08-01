using System.Collections.Generic;
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

public static class RegionRunState
{
    public const int MaxPlayerNameLength = 20;

    public static bool HasSelectedRegion { get; private set; }
    public static string RegionName { get; private set; }
    public static string SceneName { get; private set; }
    public static int RunSeed { get; private set; }
    public static RunLaunchMode LaunchMode { get; private set; } =
        RunLaunchMode.SinglePlayer;
    public static RunNetworkMode NetworkMode { get; private set; } =
        RunNetworkMode.Direct;
    public static string ConnectionAddress { get; private set; } = "127.0.0.1";
    public static ushort ConnectionPort { get; private set; } = 7777;
    public static string RelayJoinCode { get; private set; } = string.Empty;
    public static string RelayConnectionType { get; private set; } = "dtls";
    public static int RelayMaxConnections { get; private set; } = 3;

    private static readonly Dictionary<ulong, string> multiplayerPlayerNames =
        new Dictionary<ulong, string>();

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

    public static void SelectRegion(string regionName, string sceneName, int runSeed)
    {
        SelectSinglePlayerRegion(regionName, sceneName, runSeed);
    }

    public static void SelectSinglePlayerRegion(
        string regionName,
        string sceneName,
        int runSeed)
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
            3);
    }

    public static void SelectMultiplayerHostRegion(
        string regionName,
        string sceneName,
        int runSeed,
        ushort port = 7777)
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
            3);
    }

    public static void SelectMultiplayerClientRegion(
        string regionName,
        string sceneName,
        int runSeed,
        string address,
        ushort port = 7777)
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
            3);
    }

    public static void SelectRelayHostRegion(
        string regionName,
        string sceneName,
        int runSeed,
        int maxConnections = 3,
        string connectionType = "dtls")
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
            maxConnections);
    }

    public static void SelectRelayClientRegion(
        string regionName,
        string sceneName,
        int runSeed,
        string joinCode,
        string connectionType = "dtls")
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
            1);
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
        int relayMaxConnections)
    {
        HasSelectedRegion = true;
        RegionName = string.IsNullOrWhiteSpace(regionName) ? sceneName : regionName;
        SceneName = sceneName;
        RunSeed = runSeed;
        LaunchMode = launchMode;
        NetworkMode = networkMode;
        ConnectionAddress = string.IsNullOrWhiteSpace(connectionAddress)
            ? "127.0.0.1"
            : connectionAddress;
        ConnectionPort = connectionPort;
        RelayJoinCode = SanitizeRelayJoinCode(relayJoinCode);
        RelayConnectionType = SanitizeRelayConnectionType(relayConnectionType);
        RelayMaxConnections = Mathf.Max(1, relayMaxConnections);

        Debug.Log(
            $"Selected region '{RegionName}' in scene '{SceneName}' with seed {RunSeed}. Launch mode: {LaunchMode}. Network mode: {NetworkMode}.");
    }

    public static void SetRelayJoinCode(string joinCode)
    {
        RelayJoinCode = SanitizeRelayJoinCode(joinCode);
    }

    public static void SetMultiplayerPlayerNames(
        IReadOnlyDictionary<ulong, string> playerNames)
    {
        multiplayerPlayerNames.Clear();

        if (playerNames == null)
            return;

        foreach (KeyValuePair<ulong, string> playerName in playerNames)
        {
            multiplayerPlayerNames[playerName.Key] =
                SanitizePlayerName(playerName.Value, playerName.Key);
        }
    }

    public static string GetMultiplayerPlayerName(ulong clientId)
    {
        if (multiplayerPlayerNames.TryGetValue(clientId, out string playerName))
            return SanitizePlayerName(playerName, clientId);

        return GetFallbackPlayerName(clientId);
    }

    public static void Clear()
    {
        HasSelectedRegion = false;
        RegionName = string.Empty;
        SceneName = string.Empty;
        RunSeed = 0;
        LaunchMode = RunLaunchMode.SinglePlayer;
        NetworkMode = RunNetworkMode.Direct;
        ConnectionAddress = "127.0.0.1";
        ConnectionPort = 7777;
        RelayJoinCode = string.Empty;
        RelayConnectionType = "dtls";
        RelayMaxConnections = 3;
        multiplayerPlayerNames.Clear();
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

    public static string SanitizePlayerName(string playerName, ulong clientId)
    {
        string sanitized = string.IsNullOrWhiteSpace(playerName)
            ? GetFallbackPlayerName(clientId)
            : playerName.Trim();

        sanitized = sanitized.Replace('\r', ' ').Replace('\n', ' ');

        while (sanitized.Contains("  "))
            sanitized = sanitized.Replace("  ", " ");

        if (sanitized.Length > MaxPlayerNameLength)
            sanitized = sanitized.Substring(0, MaxPlayerNameLength);

        return string.IsNullOrWhiteSpace(sanitized)
            ? GetFallbackPlayerName(clientId)
            : sanitized;
    }

    public static string GetFallbackPlayerName(ulong clientId)
    {
        return clientId == 0
            ? "Host"
            : $"Player {clientId}";
    }
}
