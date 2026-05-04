using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void StartGame()
    {
        SceneManager.LoadScene(1); // loads Game scene
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