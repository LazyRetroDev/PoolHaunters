using UnityEngine;

public static class RegionRunState
{
    public static bool HasSelectedRegion { get; private set; }
    public static string RegionName { get; private set; }
    public static string SceneName { get; private set; }
    public static int RunSeed { get; private set; }

    public static void SelectRegion(string regionName, string sceneName, int runSeed)
    {
        HasSelectedRegion = true;
        RegionName = string.IsNullOrWhiteSpace(regionName) ? sceneName : regionName;
        SceneName = sceneName;
        RunSeed = runSeed;

        Debug.Log($"Selected region '{RegionName}' in scene '{SceneName}' with seed {RunSeed}.");
    }

    public static void Clear()
    {
        HasSelectedRegion = false;
        RegionName = string.Empty;
        SceneName = string.Empty;
        RunSeed = 0;
    }
}
