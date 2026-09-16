using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Survives gameplay scene transitions; lobby UI alone cannot handle a lost host.
public class NetworkSessionExitHandler : MonoBehaviour
{
    private NetworkManager manager;
    private bool connectedAsClient, returning, showNotice;
    private ulong localId;
    private static bool intentionalLeave, applicationQuitting;
    public static string MenuScene = "Menu";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
        intentionalLeave = applicationQuitting = false;
        var go = new GameObject("Network Session Exit Handler");
        DontDestroyOnLoad(go); go.AddComponent<NetworkSessionExitHandler>();
    }

    public static void BeginIntentionalLeave() { intentionalLeave = true; }

    void Update()
    {
        var next = NetworkManager.Singleton;
        if (manager != next)
        {
            if (connectedAsClient) QueueReturn();
            Unbind(); manager = next;
            if (manager != null)
            {
                manager.OnClientDisconnectCallback += Disconnected;
                manager.OnClientStopped += ClientStopped;
                manager.OnClientConnectedCallback += Connected;
            }
        }
        if (manager != null && manager.IsConnectedClient && !manager.IsServer && !returning && !intentionalLeave)
        {
            connectedAsClient = true; localId = manager.LocalClientId;
        }
        if (connectedAsClient && manager != null && !manager.IsListening && !returning) QueueReturn();
        if (showNotice && ((UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame) ||
            (UnityEngine.InputSystem.Gamepad.current != null && UnityEngine.InputSystem.Gamepad.current.buttonSouth.wasPressedThisFrame))) showNotice = false;
    }

    void Disconnected(ulong id) { if (connectedAsClient && (id == localId || id == NetworkManager.ServerClientId)) QueueReturn(); }
    void Connected(ulong id)
    {
        if (manager != null && !manager.IsServer && id == manager.LocalClientId)
        { intentionalLeave = false; connectedAsClient = true; localId = id; }
    }
    void ClientStopped(bool wasHost) { if (!wasHost && connectedAsClient) QueueReturn(); }
    void QueueReturn()
    {
        if (returning || intentionalLeave || applicationQuitting) { connectedAsClient = false; return; }
        returning = true; connectedAsClient = false;
        StartCoroutine(ReturnToMenu());
    }

    IEnumerator ReturnToMenu()
    {
        // Do not change scenes inside Netcode's disconnect callback stack.
        yield return null;
        if (manager != null && manager.IsListening) manager.Shutdown();
        float deadline = Time.realtimeSinceStartup + 5f;
        while (manager != null && manager.ShutdownInProgress && Time.realtimeSinceStartup < deadline) yield return null;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        if (Application.CanStreamedLevelBeLoaded(MenuScene))
        {
            var load = SceneManager.LoadSceneAsync(MenuScene);
            while (load != null && !load.isDone) yield return null;
        }
        showNotice = true; returning = false;
    }

    void OnGUI()
    {
        if (!showNotice) return;
        var box = new Rect((Screen.width-440)*0.5f, (Screen.height-180)*0.5f, 440,180);
        GUI.ModalWindow(GetInstanceID(), box, id =>
        {
            GUI.Label(new Rect(20,40,400,60), "The host left or the connection was lost.\nThe multiplayer session has ended.");
            if (GUI.Button(new Rect(150,115,140,40), "OK")) showNotice = false;
        }, "Disconnected");
    }

    void Unbind()
    {
        if (manager == null) return;
        manager.OnClientDisconnectCallback -= Disconnected;
        manager.OnClientStopped -= ClientStopped;
        manager.OnClientConnectedCallback -= Connected;
    }
    void OnApplicationQuit() { applicationQuitting = true; }
    void OnDestroy() { Unbind(); }
}
