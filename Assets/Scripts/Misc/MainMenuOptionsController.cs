using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MainMenuOptionsController : MonoBehaviour
{
    [Header("Navigation")]
    public GameObject optionsPanel;
    public Button backButton;

    [Header("Graphics Settings")]
    public Slider brightSlider;
    public Toggle vSyncToggle;
    public TMP_Dropdown resolutionDropDown;
    public TMP_Dropdown displayDropDown;

    private readonly Vector2Int[] CommonResolutions =
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1366, 768),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1440),
        new Vector2Int(3840, 2160)
    };

    private const string PrefPrefix = "PH_";

    private void Start()
    {
        if (backButton != null)
        {
            backButton.onClick.AddListener(() =>
            {
                if (optionsPanel != null) optionsPanel.SetActive(false);
            });
        }

        LoadSettings();
        RegisterListeners();
    }

    private void LoadSettings()
    {
        // Brightness
        if (brightSlider != null)
        {
            brightSlider.value = PlayerPrefs.GetFloat(PrefPrefix + "Brightness", 1f);
        }

        // VSync (Toggle)
        if (vSyncToggle != null)
        {
            vSyncToggle.isOn = PlayerPrefs.GetInt(PrefPrefix + "Vsync", 0) == 1;
        }

        // Display Mode
        if (displayDropDown != null)
        {
            displayDropDown.ClearOptions();
            displayDropDown.AddOptions(new List<string> { "Fullscreen", "Borderless Windowed", "Windowed" });
            displayDropDown.value = Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "DisplayMode", 1), 0, 2);
        }

        // Resolution
        if (resolutionDropDown != null)
        {
            resolutionDropDown.ClearOptions();
            List<string> resOptions = new List<string>();
            foreach (var res in CommonResolutions) resOptions.Add($"{res.x} x {res.y}");
            resolutionDropDown.AddOptions(resOptions);
            resolutionDropDown.value = Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "ResolutionIndex", 3), 0, CommonResolutions.Length - 1);
        }
    }

    private void RegisterListeners()
    {
        if (brightSlider != null)
        {
            brightSlider.onValueChanged.AddListener(v => PlayerPrefs.SetFloat(PrefPrefix + "Brightness", v));
        }

        if (vSyncToggle != null)
        {
            vSyncToggle.onValueChanged.AddListener(isOn => 
            {
                int v = isOn ? 1 : 0;
                PlayerPrefs.SetInt(PrefPrefix + "Vsync", v);
                QualitySettings.vSyncCount = v;
            });
        }

        if (displayDropDown != null)
        {
            displayDropDown.onValueChanged.AddListener(ApplyDisplayMode);
        }

        if (resolutionDropDown != null)
        {
            resolutionDropDown.onValueChanged.AddListener(ApplyResolution);
        }
    }

    private void ApplyDisplayMode(int mode)
    {
        PlayerPrefs.SetInt(PrefPrefix + "DisplayMode", mode);
        FullScreenMode fsMode = mode == 0 ? FullScreenMode.ExclusiveFullScreen : (mode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        Screen.fullScreenMode = fsMode;
    }

    private void ApplyResolution(int index)
    {
        PlayerPrefs.SetInt(PrefPrefix + "ResolutionIndex", index);
        Vector2Int res = CommonResolutions[Mathf.Clamp(index, 0, CommonResolutions.Length - 1)];
        Screen.SetResolution(res.x, res.y, Screen.fullScreenMode);
    }
}
