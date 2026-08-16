using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class ShopStartNextLevelStation : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private string readyLabel = "START NEXT LEVEL";
    [SerializeField] private string missingLevelLabel = "NO LEVEL SELECTED";
    [SerializeField] private bool onlyHostCanStart = true;
    [SerializeField] private bool allowFallbackSceneWhenNoRunState = true;
    [SerializeField] private string fallbackSceneName = "Game";
    [SerializeField] private bool transitionStarted;

    void Awake()
    {
        RefreshLabel();
    }

    void OnValidate()
    {
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);

        RefreshLabel();
    }

    public void Interact(PlayerInventory inventory)
    {
        if (transitionStarted)
            return;

        if (!CanStart())
            return;

        transitionStarted = true;
        string sceneName = GetNextSceneName();
        StartCoroutine(LoadSceneAfterInteractionFrame(sceneName));
    }

    IEnumerator LoadSceneAfterInteractionFrame(string sceneName)
    {
        yield return null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null &&
            networkManager.IsListening &&
            networkManager.SceneManager != null)
        {
            SceneEventProgressStatus status =
                networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning(
                    $"ShopStartNextLevelStation could not load scene '{sceneName}'. Scene event status: {status}.");
                transitionStarted = false;
            }

            yield break;
        }

        SceneManager.LoadScene(sceneName);
    }

    bool CanStart()
    {
        if (string.IsNullOrWhiteSpace(GetNextSceneName()))
        {
            Debug.LogWarning("Shop cannot start the next level because no next region is selected.");
            return false;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (onlyHostCanStart &&
            networkManager != null &&
            networkManager.IsListening &&
            !networkManager.IsServer)
        {
            return false;
        }

        return true;
    }

    string GetNextSceneName()
    {
        if (RegionRunState.HasSelectedRegion &&
            !string.IsNullOrWhiteSpace(RegionRunState.SceneName))
        {
            return RegionRunState.SceneName;
        }

        if (allowFallbackSceneWhenNoRunState &&
            !string.IsNullOrWhiteSpace(fallbackSceneName))
        {
            return fallbackSceneName;
        }

        return string.Empty;
    }

    void RefreshLabel()
    {
        if (label == null)
            return;

        label.text = !string.IsNullOrWhiteSpace(GetNextSceneName())
                ? readyLabel
                : missingLevelLabel;
    }
}
