using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Header("Run")]
    [SerializeField] private string regionName = "Submarino";
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int fixedSeed;

    [Header("Multiplayer")]
    [SerializeField] private string connectionAddress = "127.0.0.1";
    [SerializeField, Min(1)] private int connectionPort = 7777;

    public void StartSinglePlayer()
    {
        RegionRunState.SelectSinglePlayerRegion(regionName, gameSceneName, CreateRunSeed());
        LoadGameScene();
    }

    public void StartHost()
    {
        RegionRunState.SelectMultiplayerHostRegion(
            regionName,
            gameSceneName,
            CreateRunSeed(),
            GetConnectionPort());

        LoadGameScene();
    }

    public void StartClient()
    {
        RegionRunState.SelectMultiplayerClientRegion(
            regionName,
            gameSceneName,
            CreateRunSeed(),
            connectionAddress,
            GetConnectionPort());

        LoadGameScene();
    }

    public void SetConnectionAddress(string address)
    {
        connectionAddress = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address;
    }

    public void SetConnectionPort(string port)
    {
        if (int.TryParse(port, out int parsedPort))
            connectionPort = Mathf.Clamp(parsedPort, 1, ushort.MaxValue);
    }

    public void QuitButton()
    {
        Application.Quit();
    }

    private int CreateRunSeed()
    {
        if (!useRandomSeed)
            return fixedSeed;

        return Random.Range(1, int.MaxValue);
    }

    private ushort GetConnectionPort()
    {
        return (ushort)Mathf.Clamp(connectionPort, 1, ushort.MaxValue);
    }

    private void LoadGameScene()
    {
        if (string.IsNullOrWhiteSpace(gameSceneName))
        {
            Debug.LogError("MainMenu cannot start the run because the game scene name is empty.");
            return;
        }

        SceneManager.LoadScene(gameSceneName);
    }
}
