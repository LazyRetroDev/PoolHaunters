using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class GameSettingsManager : MonoBehaviour
{
    private const string PrefPrefix = "PH_";
    
    public static bool IsLeftHanded { get; private set; }
    public static ControllerIconStyle ControllerIcons { get; private set; } = ControllerIconStyle.Xbox;
    public static event System.Action ControllerIconsChanged;
    public static void SetControllerIcons(ControllerIconStyle style)
    {
        ControllerIcons = (ControllerIconStyle)Mathf.Clamp((int)style, 0, 2);
        PlayerPrefs.SetInt("PH_ControllerIcons", (int)ControllerIcons);
        PlayerPrefs.Save();
        ControllerIconsChanged?.Invoke();
    }
    public const string PhotosensitivePref = "PH_PhotosensitiveMode";
    public static bool PhotosensitiveMode { get; private set; }
    public static void SetPhotosensitiveMode(bool enabled)
    {
        PhotosensitiveMode = enabled;
        PlayerPrefs.SetInt(PhotosensitivePref, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    private readonly Dictionary<Renderer, float[]> retroMaterials = new Dictionary<Renderer, float[]>();
    private float nextRetroRefresh;
    private bool retroReductionApplied;
    private readonly MaterialPropertyBlock retroBlock = new MaterialPropertyBlock();

    private void Update()
    {
        if (!PhotosensitiveMode && !retroReductionApplied) return;
        if (PhotosensitiveMode == retroReductionApplied && Time.unscaledTime < nextRetroRefresh) return;
        nextRetroRefresh = Time.unscaledTime + 2f;
        if (!PhotosensitiveMode) { RestoreRetroEffects(); return; }
        retroReductionApplied = true;
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (retroMaterials.ContainsKey(renderer)) continue;
            var materials = renderer.sharedMaterials;
            float[] values = null;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || !materials[i].HasProperty("_CellSize")) continue;
                if (values == null)
                {
                    values = new float[materials.Length];
                    for (int j = 0; j < values.Length; j++) values[j] = float.NaN;
                }
                renderer.GetPropertyBlock(retroBlock, i);
                values[i] = retroBlock.HasFloat("_CellSize") ? retroBlock.GetFloat("_CellSize") : materials[i].GetFloat("_CellSize");
                // The PSX graph quantizes vertices by CellSize / 1000.
                retroBlock.SetFloat("_CellSize", Mathf.Max(0.001f, values[i] * 0.1f));
                renderer.SetPropertyBlock(retroBlock, i);
            }
            if (values != null) retroMaterials.Add(renderer, values);
        }
    }

    private void RestoreRetroEffects()
    {
        foreach (var entry in retroMaterials)
        {
            if (entry.Key == null) continue;
            for (int i = 0; i < entry.Value.Length; i++)
            {
                if (float.IsNaN(entry.Value[i])) continue;
                entry.Key.GetPropertyBlock(retroBlock, i);
                retroBlock.SetFloat("_CellSize", entry.Value[i]);
                entry.Key.SetPropertyBlock(retroBlock, i);
            }
        }
        retroMaterials.Clear();
        retroReductionApplied = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        requestedWidth = requestedHeight = 0;
        requestedMode = -1;
        ControllerIcons = (ControllerIconStyle)Mathf.Clamp(PlayerPrefs.GetInt("PH_ControllerIcons", 0), 0, 2);
        PhotosensitiveMode = PlayerPrefs.GetInt(PhotosensitivePref, 0) == 1;
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
        RestoreRetroEffects();
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
        
        ApplyDisplaySettings();

        ApplyBrightness();

        // 3. Player Ops
        IsLeftHanded = PlayerPrefs.GetInt(PrefPrefix + "LeftHanded", 0) == 1;
    }

    private static int requestedWidth, requestedHeight, requestedMode = -1;
    public static int DisplayModeIndex => Mathf.Clamp(PlayerPrefs.GetInt("PH_DisplayMode", 1), 0, 2);
    public static int ResolutionIndex => Mathf.Clamp(PlayerPrefs.GetInt("PH_ResolutionIndex", 3), 0, 5);
    private static readonly Vector2Int[] DisplayResolutions = { new Vector2Int(1280,720), new Vector2Int(1366,768), new Vector2Int(1600,900), new Vector2Int(1920,1080), new Vector2Int(2560,1440), new Vector2Int(3840,2160) };

    public static void SaveDisplayMode(int mode)
    {
        PlayerPrefs.SetInt("PH_DisplayMode", Mathf.Clamp(mode, 0, 2));
        PlayerPrefs.Save(); ApplyDisplaySettings();
    }
    public static void SaveResolution(int index)
    {
        PlayerPrefs.SetInt("PH_ResolutionIndex", Mathf.Clamp(index, 0, 5));
        PlayerPrefs.Save(); ApplyDisplaySettings();
    }
    public static void ApplyDisplaySettings()
    {
        Vector2Int size = DisplayResolutions[ResolutionIndex];
        int mode = DisplayModeIndex;
        if (requestedWidth == size.x && requestedHeight == size.y && requestedMode == mode) return;
        requestedWidth = size.x; requestedHeight = size.y; requestedMode = mode;
        Screen.SetResolution(size.x, size.y, mode == 0 ? FullScreenMode.ExclusiveFullScreen : mode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
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
        var owner = pi.GetComponent<PlayerMovement>();
        if (owner != null && owner.IsSpawned && !owner.IsOwner) return;

        OverrideAction(pi.actions, "Move", "up", PlayerPrefs.GetString(PrefPrefix + "Bind_MoveForward", "W"));
        OverrideAction(pi.actions, "Move", "down", PlayerPrefs.GetString(PrefPrefix + "Bind_MoveBackward", "S"));
        OverrideAction(pi.actions, "Move", "left", PlayerPrefs.GetString(PrefPrefix + "Bind_MoveLeft", "A"));
        OverrideAction(pi.actions, "Move", "right", PlayerPrefs.GetString(PrefPrefix + "Bind_MoveRight", "D"));
        
        OverrideAction(pi.actions, "Jump", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Jump", "Space"));
        OverrideAction(pi.actions, "Sprint", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Run", "LeftShift"));
        OverrideAction(pi.actions, "Crouch", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Crouch", "LeftCtrl"));
        OverrideAction(pi.actions, "Interact", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Interact", "E"));
        OverrideAction(pi.actions, "Use", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Use", "f"));
        OverrideAction(pi.actions, "Attack", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Attack", "leftButton"));
        OverrideAction(pi.actions, "Throw", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Throw", "g"));
        OverrideAction(pi.actions, "Ability", "", PlayerPrefs.GetString(PrefPrefix + "Bind_Ability", "q"));
        OverrideAction(pi.actions, "RightClick", "", PlayerPrefs.GetString(PrefPrefix + "Bind_RightClick", "rightButton"));
        for (int i = 1; i <= 4; i++)
            OverrideAction(pi.actions, "Slot" + i, "", PlayerPrefs.GetString(PrefPrefix + "Bind_Slot" + i, i.ToString()));
        for (int n = 0; n < ControllerActions.Length; n++)
        {
            var action = pi.actions.FindAction("Player/" + ControllerActions[n]);
            if (action == null) continue;
            for (int i = 0; i < action.bindings.Count; i++)
                if (action.bindings[i].path.StartsWith("<Gamepad>/"))
                { action.ApplyBindingOverride(i, "<Gamepad>/" + GetControllerBinding(n)); break; }
        }
    }

    private static void OverrideAction(InputActionAsset actions, string actionName, string bindingName, string systemKeyName)
    {
        if (string.IsNullOrEmpty(systemKeyName)) return;

        InputAction action = actions.FindAction("Player/" + actionName);
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
            // Preserve gamepad/XR bindings even when they appear first.
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (binding.isComposite || binding.isPartOfComposite || string.IsNullOrEmpty(binding.path)) continue;
                if (!binding.path.StartsWith("<Keyboard>/") && !binding.path.StartsWith("<Mouse>/")) continue;
                action.ApplyBindingOverride(i, path);
                break;
            }
        }
    }

    public static readonly string[] BindingLabels = { "FORWARD", "BACKWARD", "LEFT", "RIGHT", "JUMP", "SPRINT", "CROUCH", "INTERACT", "USE ITEM", "FIRE WATER", "THROW", "SLOT 1", "SLOT 2", "SLOT 3", "SLOT 4", "ABILITY / DASH", "SECONDARY / SPLASH" };
    public static readonly string[] BindingKeys = { "MoveForward", "MoveBackward", "MoveLeft", "MoveRight", "Jump", "Run", "Crouch", "Interact", "Use", "Attack", "Throw", "Slot1", "Slot2", "Slot3", "Slot4", "Ability", "RightClick" };
    public static readonly string[] BindingDefaults = { "w", "s", "a", "d", "space", "leftShift", "leftCtrl", "e", "f", "leftButton", "g", "1", "2", "3", "4", "q", "rightButton" };

    public static string GetBinding(int index) => PlayerPrefs.GetString(PrefPrefix + "Bind_" + BindingKeys[index], BindingDefaults[index]);

    public static bool SaveBinding(int index, string key, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(key) || key.Equals("escape", System.StringComparison.OrdinalIgnoreCase))
        { error = "Escape is reserved for the menu"; return false; }
        for (int i = 0; i < BindingKeys.Length; i++)
            if (i != index && GetBinding(i).Equals(key, System.StringComparison.OrdinalIgnoreCase))
            { error = "Already assigned to " + BindingLabels[i]; return false; }
        PlayerPrefs.SetString(PrefPrefix + "Bind_" + BindingKeys[index], key);
        PlayerPrefs.Save();
        ApplyBindingsNow();
        return true;
    }

    public static void ResetBindings()
    {
        foreach (string key in BindingKeys) PlayerPrefs.DeleteKey(PrefPrefix + "Bind_" + key);
        PlayerPrefs.Save();
        ApplyBindingsNow();
    }

    public static readonly string[] ControllerLabels = { "JUMP", "SPRINT", "CROUCH", "INTERACT", "USE ITEM", "FIRE WATER", "THROW", "PREVIOUS ITEM", "NEXT ITEM", "ABILITY / DASH", "SECONDARY / SPLASH" };
    public static readonly string[] ControllerActions = { "Jump", "Sprint", "Crouch", "Interact", "Use", "Attack", "Throw", "Previous", "Next", "Ability", "RightClick" };
    public static readonly string[] ControllerDefaults = { "buttonSouth", "leftStickPress", "buttonEast", "buttonNorth", "buttonWest", "rightTrigger", "rightShoulder", "dpad/left", "dpad/right", "leftShoulder", "leftTrigger" };
    public static readonly string[] ControllerButtons = { "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder", "leftTrigger", "rightTrigger", "leftStickPress", "rightStickPress", "dpad/up", "dpad/down", "dpad/left", "dpad/right" };
    public static string GetControllerBinding(int index) => PlayerPrefs.GetString("PH_Pad_" + ControllerActions[index], ControllerDefaults[index]);
    public static bool SaveControllerBinding(int index, string button, out string error)
    {
        error = null;
        if (System.Array.IndexOf(ControllerButtons, button) < 0) { error = "Choose a gamepad button or trigger"; return false; }
        for (int i = 0; i < ControllerActions.Length; i++)
            if (i != index && GetControllerBinding(i) == button)
            { error = "Already assigned to " + ControllerLabels[i]; return false; }
        PlayerPrefs.SetString("PH_Pad_" + ControllerActions[index], button);
        PlayerPrefs.Save(); ApplyBindingsNow(); return true;
    }
    public static void ResetControllerBindings()
    {
        foreach (string action in ControllerActions) PlayerPrefs.DeleteKey("PH_Pad_" + action);
        PlayerPrefs.Save(); ApplyBindingsNow();
    }

    public static void ApplyBindingsNow()
    {
        foreach (var pi in FindObjectsByType<PlayerInput>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            ApplyInputOverridesToPlayer(pi);
    }
}
