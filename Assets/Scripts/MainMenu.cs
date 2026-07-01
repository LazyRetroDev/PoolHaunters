using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Serializable]
    public class RegionSceneEntry
    {
        public string regionName = "Hospital";
        public string sceneName = "Game";
        [Min(0f)] public float weight = 1f;
    }

    [Header("Run Start")]
    public RegionSceneEntry[] startingRegions =
    {
        new RegionSceneEntry { regionName = "Hospital", sceneName = "Game", weight = 1f }
    };

    public bool randomizeRunSeed = true;
    public int fixedRunSeed = 0;

    [Header("Lobby")]
    public bool useLobbyMenuMockup = true;

    public void StartGame()
    {
        if (useLobbyMenuMockup)
        {
            LobbyMenuMockup.Show(this);
            return;
        }

        BeginGame(null);
    }

    public void StartGameFromLobby(string requestedRegion)
    {
        BeginGame(requestedRegion);
    }

    void BeginGame(string requestedRegion)
    {
        RegionSceneEntry selectedRegion = FindRegion(requestedRegion);
        if (selectedRegion == null)
            selectedRegion = ChooseStartingRegion();

        int runSeed = randomizeRunSeed ? UnityEngine.Random.Range(int.MinValue, int.MaxValue) : fixedRunSeed;

        if (selectedRegion == null || string.IsNullOrWhiteSpace(selectedRegion.sceneName))
        {
            RegionRunState.SelectRegion("Hospital", "Game", runSeed);
            SceneManager.LoadScene("Game");
            return;
        }

        RegionRunState.SelectRegion(selectedRegion.regionName, selectedRegion.sceneName, runSeed);
        SceneManager.LoadScene(selectedRegion.sceneName);
    }

    RegionSceneEntry FindRegion(string requestedRegion)
    {
        if (string.IsNullOrWhiteSpace(requestedRegion) ||
            requestedRegion.Equals("Random", StringComparison.OrdinalIgnoreCase) ||
            startingRegions == null)
        {
            return null;
        }

        for (int i = 0; i < startingRegions.Length; i++)
        {
            RegionSceneEntry entry = startingRegions[i];
            if (entry != null &&
                entry.regionName.Equals(
                    requestedRegion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    RegionSceneEntry ChooseStartingRegion()
    {
        if (startingRegions == null || startingRegions.Length == 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < startingRegions.Length; i++)
        {
            RegionSceneEntry entry = startingRegions[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName)) continue;
            totalWeight += Mathf.Max(0f, entry.weight);
        }

        if (totalWeight <= 0f)
            return startingRegions[0];

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        for (int i = 0; i < startingRegions.Length; i++)
        {
            RegionSceneEntry entry = startingRegions[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName)) continue;

            roll -= Mathf.Max(0f, entry.weight);
            if (roll <= 0f)
                return entry;
        }

        return startingRegions[0];
    }

    public void OpenOptions()
    {
        // You can load an Options scene or toggle a panel here
        Debug.Log("Options opened");
    }

    public void QuitGame()
    {
        Application.Quit();
        Debug.Log("Quit"); // only shows in Editor
    }
}
