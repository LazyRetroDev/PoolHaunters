using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class GameSettingsManager : MonoBehaviour
{
    private const string PrefPrefix = "PH_";
    
    public static bool IsLeftHanded { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        GameObject go = new GameObject("[GameSettingsManager]");
        DontDestroyOnLoad(go);
        go.AddComponent<GameSettingsManager>();
    }

    private void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyGlobalSettings();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyGlobalSettings();
        ApplyInputOverrides();
    }

    private static UnityEngine.UI.Image brightnessOverlay;

    public static void ApplyGlobalSettings()
    {
        // 1. Audio
        AudioListener.volume = PlayerPrefs.GetFloat(PrefPrefix + "MasterVolume", 1f);

        // 2. Graphics (Vsync / Display / Brightness)
        QualitySettings.vSyncCount = PlayerPrefs.GetInt(PrefPrefix + "Vsync", 0);
        
        int displayMode = PlayerPrefs.GetInt(PrefPrefix + "DisplayMode", 1);
        FullScreenMode fsMode = displayMode == 0 ? FullScreenMode.ExclusiveFullScreen : (displayMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        Screen.fullScreenMode = fsMode;

        ApplyBrightness();

        // 3. Player Ops
        IsLeftHanded = PlayerPrefs.GetInt(PrefPrefix + "LeftHanded", 0) == 1;
    }

    public static void ApplyBrightness()
    {
        if (brightnessOverlay == null)
        {
            GameObject canvasObj = new GameObject("[BrightnessCanvas]");
            DontDestroyOnLoad(canvasObj);
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767; // Highest possible to draw over everything
            
            GameObject imgObj = new GameObject("BrightnessOverlay");
            imgObj.transform.SetParent(canvasObj.transform, false);
            brightnessOverlay = imgObj.AddComponent<UnityEngine.UI.Image>();
            brightnessOverlay.color = Color.clear;
            brightnessOverlay.raycastTarget = false;
            
            RectTransform rt = imgObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
        }

        // Original game's logic: 1 = clear (alpha 0), 0 = completely black (alpha 1)
        float b = PlayerPrefs.GetFloat(PrefPrefix + "Brightness", 1f);
        float alpha = Mathf.Clamp01(1f - b);
        brightnessOverlay.color = new Color(0, 0, 0, alpha);
    }

    private void ApplyInputOverrides()
    {
        PlayerInput[] allInputs = FindObjectsByType<PlayerInput>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        
        foreach (var pi in allInputs)
        {
            ApplyInputOverridesToPlayer(pi);
        }
    }

    public static void ApplyInputOverridesToPlayer(PlayerInput pi)
    {
        if (pi == null || pi.actions == null) return;

        OverrideAction(pi.actions, "Move", "up", PlayerPrefs.GetString(PrefPrefix + "Bind_MoveForward", "W"));
        OverrideAction(pi.actions, "Move", "down", PlayerPrefs.GetString(PrefPrefix + "Bind_MoveBackward", "S"));
        OverrideAction(pi.actions, "Move", "left", PlayerPrefs.GetString(PrefPrefix + "Bind_MoveLeft", "A"));
        OverrideAction(pi.actions, "Move", "right", PlayerPrefs.GetString(PrefPrefix + "Bind_MoveRight", "D"));
        
        OverrideAction(pi.actions, "Jump", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Jump", "Space"));
        OverrideAction(pi.actions, "Sprint", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Run", "LeftShift"));
        OverrideAction(pi.actions, "Crouch", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Crouch", "LeftCtrl"));
        OverrideAction(pi.actions, "Interact", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Interact", "E"));
        OverrideAction(pi.actions, "Use", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Use", "LeftButton"));
    }

    private static void OverrideAction(InputActionAsset actions, string actionName, string bindingName, string systemKeyName)
    {
        if (string.IsNullOrEmpty(systemKeyName)) return;

        InputAction action = actions.FindAction(actionName);
        if (action == null) return;

        string path = $"<Keyboard>/{systemKeyName.ToLower()}";
        
        // Handle mouse buttons specifically
        if (systemKeyName.ToLower().Contains("button"))
        {
            path = $"<Mouse>/{systemKeyName.ToLower()}";
        }

        // If bindingName is provided, we only override the specific composite part (e.g. WASD "up")
        if (!string.IsNullOrEmpty(bindingName))
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (action.bindings[i].name.Equals(bindingName, System.StringComparison.OrdinalIgnoreCase))
                {
                    action.ApplyBindingOverride(i, path);
                    break;
                }
            }
        }
        else
        {
            // Simple action, override the first binding
            if (action.bindings.Count > 0)
            {
                action.ApplyBindingOverride(0, path);
            }
        }
    }
}
