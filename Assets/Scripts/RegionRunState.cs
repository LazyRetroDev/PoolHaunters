using UnityEngine;

public enum RunLaunchMode
{
    SinglePlayer,
    MultiplayerHost,
    MultiplayerClient
}

public static class RegionRunState
{
    public static bool HasSelectedRegion { get; private set; }
    public static string RegionName { get; private set; }
    public static string SceneName { get; private set; }
    public static int RunSeed { get; private set; }
    public static RunLaunchMode LaunchMode { get; private set; } =
        RunLaunchMode.SinglePlayer;
    public static string ConnectionAddress { get; private set; } = "127.0.0.1";
    public static ushort ConnectionPort { get; private set; } = 7777;

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
            "127.0.0.1",
            7777);
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
            "127.0.0.1",
            port);
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
            address,
            port);
    }

    static void SelectRegion(
        string regionName,
        string sceneName,
        int runSeed,
        RunLaunchMode launchMode,
        string connectionAddress,
        ushort connectionPort)
    {
        HasSelectedRegion = true;
        RegionName = string.IsNullOrWhiteSpace(regionName) ? sceneName : regionName;
        SceneName = sceneName;
        RunSeed = runSeed;
        LaunchMode = launchMode;
        ConnectionAddress = string.IsNullOrWhiteSpace(connectionAddress)
            ? "127.0.0.1"
            : connectionAddress;
        ConnectionPort = connectionPort;

        Debug.Log(
            $"Selected region '{RegionName}' in scene '{SceneName}' with seed {RunSeed}. Launch mode: {LaunchMode}.");
    }

    public static void Clear()
    {
        HasSelectedRegion = false;
        RegionName = string.Empty;
        SceneName = string.Empty;
        RunSeed = 0;
        LaunchMode = RunLaunchMode.SinglePlayer;
        ConnectionAddress = "127.0.0.1";
        ConnectionPort = 7777;
    }
}
