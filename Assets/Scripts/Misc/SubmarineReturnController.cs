using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class SubmarineReturnController : MonoBehaviour
{
    [Header("Return Rules")]
    public bool requireLevelCompleted = true;
    public bool onlyPlayersCanTrigger = true;
    public bool autoAddTriggerCollider = true;
    public Vector3 fallbackTriggerSize = new Vector3(3f, 3f, 3f);

    [Header("Shop Flow")]
    public bool routeThroughPhysicalShop = true;
    public string shopSceneName = "Shop";

    [Header("Next Phase Scene Choice")]
    public RunSceneOption[] sceneOptions =
    {
        new RunSceneOption { regionName = "Hospital", sceneName = "Game", weight = 1f },
        new RunSceneOption { regionName = "Museum", sceneName = "Game 1", weight = 1f },
        new RunSceneOption { regionName = "Hotel", sceneName = "Game 2", weight = 1f }
    };
    public bool blockCurrentScene = true;
    public bool blockPreviousScene = false;
    public bool useRandomSeed = true;
    public int fixedSeed;

    [Header("Debug")]
    [SerializeField] private bool transitionStarted;
    [SerializeField] private string selectedNextScene;
    [SerializeField] private string selectedDestinationScene;

    void Reset()
    {
        EnsureTriggerCollider();
    }

    void Awake()
    {
        if (autoAddTriggerCollider)
            EnsureTriggerCollider();
    }

    void OnTriggerEnter(Collider other)
    {
        TryReturnToSubmarine(other);
    }

    public void TryReturnToSubmarine(Collider other)
    {
        if (transitionStarted)
            return;

        if (requireLevelCompleted &&
            (LevelObjectiveManager.Instance == null ||
             !LevelObjectiveManager.Instance.LevelCompleted))
        {
            return;
        }

        if (onlyPlayersCanTrigger && !IsPlayerCollider(other))
            return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening && !networkManager.IsServer)
            return;

        RunSceneOption nextScene = ChooseNextScene();
        if (nextScene == null || string.IsNullOrWhiteSpace(nextScene.sceneName))
        {
            Debug.LogWarning("SubmarineReturnController could not choose a next scene.");
            return;
        }

        transitionStarted = true;
        selectedNextScene = nextScene.sceneName;
        selectedDestinationScene = GetDestinationSceneName(nextScene);

        RegionRunState.SelectNextPhaseRegion(
            string.IsNullOrWhiteSpace(nextScene.regionName)
                ? nextScene.sceneName
                : nextScene.regionName,
            nextScene.sceneName,
            CreateRunSeed());

        if (networkManager != null &&
            networkManager.IsListening &&
            networkManager.SceneManager != null)
        {
            networkManager.SceneManager.LoadScene(
                selectedDestinationScene,
                LoadSceneMode.Single);
            return;
        }

        SceneManager.LoadScene(selectedDestinationScene);
    }

    string GetDestinationSceneName(RunSceneOption nextScene)
    {
        if (routeThroughPhysicalShop && !string.IsNullOrWhiteSpace(shopSceneName))
            return shopSceneName;

        return nextScene != null ? nextScene.sceneName : string.Empty;
    }

    RunSceneOption ChooseNextScene()
    {
        if (sceneOptions == null || sceneOptions.Length == 0)
            return null;

        string currentScene = GetCurrentSceneName();
        string previousScene = RegionRunState.PreviousSceneName;

        float totalWeight = 0f;
        for (int i = 0; i < sceneOptions.Length; i++)
        {
            RunSceneOption option = sceneOptions[i];
            if (!CanUseSceneOption(option, currentScene, previousScene))
                continue;

            totalWeight += Mathf.Max(0f, option.weight);
        }

        if (totalWeight <= 0f)
            return FindFirstUsableSceneOption(ignoreSceneBlocks: true);

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        for (int i = 0; i < sceneOptions.Length; i++)
        {
            RunSceneOption option = sceneOptions[i];
            if (!CanUseSceneOption(option, currentScene, previousScene))
                continue;

            roll -= Mathf.Max(0f, option.weight);
            if (roll <= 0f)
                return option;
        }

        return FindFirstUsableSceneOption(ignoreSceneBlocks: false);
    }

    bool CanUseSceneOption(
        RunSceneOption option,
        string currentScene,
        string previousScene)
    {
        if (option == null ||
            string.IsNullOrWhiteSpace(option.sceneName) ||
            option.weight <= 0f)
        {
            return false;
        }

        if (blockCurrentScene &&
            string.Equals(option.sceneName, currentScene, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (blockPreviousScene &&
            string.Equals(option.sceneName, previousScene, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    RunSceneOption FindFirstUsableSceneOption(bool ignoreSceneBlocks)
    {
        for (int i = 0; i < sceneOptions.Length; i++)
        {
            RunSceneOption option = sceneOptions[i];
            if (option == null ||
                string.IsNullOrWhiteSpace(option.sceneName) ||
                option.weight <= 0f)
            {
                continue;
            }

            if (ignoreSceneBlocks ||
                CanUseSceneOption(option, GetCurrentSceneName(), RegionRunState.PreviousSceneName))
            {
                return option;
            }
        }

        return null;
    }

    int CreateRunSeed()
    {
        if (!useRandomSeed)
            return fixedSeed;

        unchecked
        {
            return Environment.TickCount ^
                DateTime.UtcNow.Millisecond ^
                UnityEngine.Random.Range(0, int.MaxValue);
        }
    }

    string GetCurrentSceneName()
    {
        if (RegionRunState.HasSelectedRegion &&
            !string.IsNullOrWhiteSpace(RegionRunState.SceneName))
        {
            return RegionRunState.SceneName;
        }

        return SceneManager.GetActiveScene().name;
    }

    bool IsPlayerCollider(Collider other)
    {
        if (other == null)
            return false;

        return other.GetComponentInParent<PlayerStatus>() != null ||
            other.GetComponentInParent<PlayerInventory>() != null;
    }

    void EnsureTriggerCollider()
    {
        Collider existingCollider = GetComponent<Collider>();
        if (existingCollider != null)
        {
            existingCollider.isTrigger = true;
            return;
        }

        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = fallbackTriggerSize;
    }
}
