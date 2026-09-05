using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

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

    [Header("Audio Settings")]
    public Slider masterVolumeSlider;
    public TMP_Dropdown audioDeviceDropDown;

    [Header("Microphone Settings")]
    public TMP_Dropdown micDeviceDropDown;
    public Slider micVolumeSlider;
    public Toggle pushToTalkToggle;
    public Slider micTestSlider;
    public Button testMicButton;

    [Header("Input Settings (Binds)")]
    public TMP_InputField moveForwardInputField;
    public TMP_InputField moveBackwardInputField;
    public TMP_InputField moveLeftInputField;
    public TMP_InputField moveRightInputField;
    public TMP_InputField jumpInputField;
    public TMP_InputField runInputField;
    public TMP_InputField crouchInputField;
    public TMP_InputField interactInputField;
    public TMP_InputField useInputField;

    [Header("Player Settings")]
    public TMP_Dropdown languageDropDown;
    public Toggle leftHandedToggle;

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
    private Coroutine rebindCoroutine;

    private AudioClip micTestClip;
    private string currentMicDevice;
    private bool isTestingMic = false;
    private AudioSource micAudioSource;

    private void Start()
    {
        if (backButton != null)
        {
            backButton.onClick.AddListener(() =>
            {
                if (optionsPanel != null) optionsPanel.SetActive(false);
            });
        }

        if (testMicButton != null)
        {
            testMicButton.onClick.AddListener(ToggleMicTest);
        }

        LoadSettings();
        RegisterListeners();
    }

    private void ToggleMicTest()
    {
        isTestingMic = !isTestingMic;
        
        if (isTestingMic)
        {
            StartMicTest();
        }
        else
        {
            StopMicTest();
        }
    }

    private void StartMicTest()
    {
        if (Microphone.devices.Length == 0) return;

        int deviceIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "MicDeviceIndex", 0), 0, Microphone.devices.Length - 1);
        currentMicDevice = Microphone.devices[deviceIndex];

        if (micAudioSource == null)
        {
            micAudioSource = gameObject.AddComponent<AudioSource>();
            micAudioSource.loop = true;
        }

        micTestClip = Microphone.Start(currentMicDevice, true, 1, 44100);
        micAudioSource.clip = micTestClip;
        
        StartCoroutine(WaitForMicToStartAndPlay());
    }

    private IEnumerator WaitForMicToStartAndPlay()
    {
        // Wait until the microphone has recorded a safe buffer of samples (e.g., 0.1 seconds)
        // This prevents the read head from catching up to the write head, which causes distortion.
        int safeBuffer = 4410; 
        while (Microphone.GetPosition(currentMicDevice) < safeBuffer)
        {
            yield return null;
        }

        if (micAudioSource != null && micTestClip != null)
        {
            // Apply the volume slider setting to the local playback
            float currentVolume = PlayerPrefs.GetFloat(PrefPrefix + "MicVolume", 1f);
            if (micVolumeSlider != null)
            {
                currentVolume = micVolumeSlider.value;
            }
            micAudioSource.volume = currentVolume;

            micAudioSource.Play();
        }
    }

    private void StopMicTest()
    {
        isTestingMic = false;

        if (micTestSlider != null) micTestSlider.value = 0f;

        if (micAudioSource != null)
        {
            micAudioSource.Stop();
        }

        if (currentMicDevice != null && Microphone.IsRecording(currentMicDevice))
        {
            Microphone.End(currentMicDevice);
        }

        micTestClip = null;
        currentMicDevice = null;
    }

    private void LoadSettings()
    {
        // Graphics
        if (brightSlider != null) brightSlider.value = PlayerPrefs.GetFloat(PrefPrefix + "Brightness", 1f);
        if (vSyncToggle != null) vSyncToggle.isOn = PlayerPrefs.GetInt(PrefPrefix + "Vsync", 0) == 1;

        if (displayDropDown != null)
        {
            displayDropDown.ClearOptions();
            displayDropDown.AddOptions(new List<string> { "Fullscreen", "Borderless Windowed", "Windowed" });
            displayDropDown.value = Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "DisplayMode", 1), 0, 2);
        }

        if (resolutionDropDown != null)
        {
            resolutionDropDown.ClearOptions();
            List<string> resOptions = new List<string>();
            foreach (var res in CommonResolutions) resOptions.Add($"{res.x} x {res.y}");
            resolutionDropDown.AddOptions(resOptions);
            resolutionDropDown.value = Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "ResolutionIndex", 3), 0, CommonResolutions.Length - 1);
        }

        // Audio Output
        if (masterVolumeSlider != null) masterVolumeSlider.value = PlayerPrefs.GetFloat(PrefPrefix + "MasterVolume", 1f);
        if (audioDeviceDropDown != null)
        {
            audioDeviceDropDown.ClearOptions();
            audioDeviceDropDown.AddOptions(new List<string> { "Default System Device" });
            audioDeviceDropDown.value = PlayerPrefs.GetInt(PrefPrefix + "AudioDevice", 0);
        }

        // Microphone
        if (micVolumeSlider != null) micVolumeSlider.value = PlayerPrefs.GetFloat(PrefPrefix + "MicVolume", 1f);
        if (pushToTalkToggle != null) pushToTalkToggle.isOn = PlayerPrefs.GetInt(PrefPrefix + "PushToTalk", 1) == 1;
        if (micTestSlider != null)
        {
            micTestSlider.interactable = false;
            micTestSlider.value = 0f;
        }

        if (micDeviceDropDown != null)
        {
            micDeviceDropDown.ClearOptions();
            List<string> micOptions = new List<string>();
            if (Microphone.devices.Length > 0)
            {
                micOptions.AddRange(Microphone.devices);
            }
            else
            {
                micOptions.Add("No Microphone Found");
            }
            micDeviceDropDown.AddOptions(micOptions);
            micDeviceDropDown.value = Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "MicDeviceIndex", 0), 0, Mathf.Max(0, micOptions.Count - 1));
        }

        // Input Binds
        LoadBind(moveForwardInputField, "Bind_MoveForward", "W");
        LoadBind(moveBackwardInputField, "Bind_MoveBackward", "S");
        LoadBind(moveLeftInputField, "Bind_MoveLeft", "A");
        LoadBind(moveRightInputField, "Bind_MoveRight", "D");
        LoadBind(jumpInputField, "Bind_Jump", "Space");
        LoadBind(runInputField, "Bind_Run", "LeftShift");
        LoadBind(crouchInputField, "Bind_Crouch", "LeftCtrl");
        LoadBind(interactInputField, "Bind_Interact", "E");
        LoadBind(useInputField, "Bind_Use", "LeftButton");

        // Player Ops
        if (languageDropDown != null)
        {
            languageDropDown.ClearOptions();
            languageDropDown.AddOptions(GameLocalization.BuildDisplayNameList());
            languageDropDown.value = GameLocalization.CurrentLanguageIndex;
        }
        if (leftHandedToggle != null) leftHandedToggle.isOn = PlayerPrefs.GetInt(PrefPrefix + "LeftHanded", 0) == 1;
    }

    private void RegisterListeners()
    {
        // Graphics
        if (brightSlider != null) 
        {
            brightSlider.onValueChanged.AddListener(v => 
            {
                PlayerPrefs.SetFloat(PrefPrefix + "Brightness", v);
                GameSettingsManager.ApplyBrightness(); // Apply instantly
            });
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

        if (displayDropDown != null) displayDropDown.onValueChanged.AddListener(ApplyDisplayMode);
        if (resolutionDropDown != null) resolutionDropDown.onValueChanged.AddListener(ApplyResolution);

        // Audio Output
        if (masterVolumeSlider != null) 
        {
            masterVolumeSlider.onValueChanged.AddListener(v => 
            {
                PlayerPrefs.SetFloat(PrefPrefix + "MasterVolume", v);
                AudioListener.volume = v; // Apply instantly
            });
        }
        if (audioDeviceDropDown != null) audioDeviceDropDown.onValueChanged.AddListener(v => PlayerPrefs.SetInt(PrefPrefix + "AudioDevice", v));

        // Microphone
        if (micVolumeSlider != null) 
        {
            micVolumeSlider.onValueChanged.AddListener(v => 
            {
                PlayerPrefs.SetFloat(PrefPrefix + "MicVolume", v);
                if (isTestingMic && micAudioSource != null)
                {
                    micAudioSource.volume = v;
                }
            });
        }
        if (pushToTalkToggle != null) pushToTalkToggle.onValueChanged.AddListener(isOn => PlayerPrefs.SetInt(PrefPrefix + "PushToTalk", isOn ? 1 : 0));
        if (micDeviceDropDown != null) micDeviceDropDown.onValueChanged.AddListener(v => PlayerPrefs.SetInt(PrefPrefix + "MicDeviceIndex", v));

        // Input Binds
        RegisterBindListener(moveForwardInputField, "Bind_MoveForward");
        RegisterBindListener(moveBackwardInputField, "Bind_MoveBackward");
        RegisterBindListener(moveLeftInputField, "Bind_MoveLeft");
        RegisterBindListener(moveRightInputField, "Bind_MoveRight");
        RegisterBindListener(jumpInputField, "Bind_Jump");
        RegisterBindListener(runInputField, "Bind_Run");
        RegisterBindListener(crouchInputField, "Bind_Crouch");
        RegisterBindListener(interactInputField, "Bind_Interact");
        RegisterBindListener(useInputField, "Bind_Use");

        // Player Ops
        if (languageDropDown != null) languageDropDown.onValueChanged.AddListener(v => GameLocalization.SetLanguageIndex(v));
        if (leftHandedToggle != null) leftHandedToggle.onValueChanged.AddListener(isOn => PlayerPrefs.SetInt(PrefPrefix + "LeftHanded", isOn ? 1 : 0));
    }

    private void LoadBind(TMP_InputField field, string prefKey, string defaultKey)
    {
        if (field == null) return;
        field.readOnly = true; // Prevent normal typing
        
        string savedKey = PlayerPrefs.GetString(PrefPrefix + prefKey, defaultKey);
        field.text = FormatKeyName(savedKey);
    }

    private void RegisterBindListener(TMP_InputField field, string prefKey)
    {
        if (field == null) return;
        
        // Remove standard editing capabilities by making it readonly
        field.readOnly = true;

        // When the user clicks the field, start rebinding
        field.onSelect.AddListener((_) => 
        {
            if (rebindCoroutine != null) StopCoroutine(rebindCoroutine);
            rebindCoroutine = StartCoroutine(RebindRoutine(field, prefKey));
        });
    }

    private IEnumerator RebindRoutine(TMP_InputField field, string prefKey)
    {
        field.text = "Press any key...";
        
        // Wait a frame so we don't instantly register the mouse click that focused the field
        yield return null;

        bool keyBound = false;
        while (!keyBound)
        {
            // Check Keyboard
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            {
                foreach (var key in Keyboard.current.allKeys)
                {
                    if (key.wasPressedThisFrame)
                    {
                        ApplyBind(field, prefKey, key.name);
                        keyBound = true;
                        break;
                    }
                }
            }
            // Check Mouse
            else if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame) { ApplyBind(field, prefKey, "LeftButton"); keyBound = true; }
                else if (Mouse.current.rightButton.wasPressedThisFrame) { ApplyBind(field, prefKey, "RightButton"); keyBound = true; }
                else if (Mouse.current.middleButton.wasPressedThisFrame) { ApplyBind(field, prefKey, "MiddleButton"); keyBound = true; }
            }

            yield return null;
        }

        EventSystem.current.SetSelectedGameObject(null);
    }

    private void ApplyBind(TMP_InputField field, string prefKey, string systemName)
    {
        PlayerPrefs.SetString(PrefPrefix + prefKey, systemName);
        field.text = FormatKeyName(systemName);
    }

    private string FormatKeyName(string systemName)
    {
        if (string.IsNullOrEmpty(systemName)) return "";
        
        if (systemName.ToLower() == "space") return "Spacebar";
        if (systemName.ToLower() == "leftshift") return "Left Shift";
        if (systemName.ToLower() == "leftctrl") return "Left Ctrl";
        if (systemName.ToLower() == "leftalt") return "Left Alt";
        if (systemName.ToLower() == "leftbutton") return "Left Mouse Button";
        if (systemName.ToLower() == "rightbutton") return "Right Mouse Button";
        if (systemName.ToLower() == "middlebutton") return "Middle Mouse Button";
        if (systemName.ToLower() == "escape") return "Esc";

        return char.ToUpper(systemName[0]) + systemName.Substring(1);
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

    private void Update()
    {
        // Mic Test Logic
        if (isTestingMic && micTestSlider != null && micTestClip != null)
        {
            int sampleWindow = 128;
            float[] waveData = new float[sampleWindow];
            int micPosition = Microphone.GetPosition(currentMicDevice) - (sampleWindow + 1);
            if (micPosition >= 0)
            {
                micTestClip.GetData(waveData, micPosition);
                float maxLevel = 0f;
                for (int i = 0; i < sampleWindow; i++)
                {
                    if (Mathf.Abs(waveData[i]) > maxLevel) maxLevel = Mathf.Abs(waveData[i]);
                }
                micTestSlider.value = Mathf.Lerp(micTestSlider.value, maxLevel, Time.deltaTime * 15f);
            }
        }
        
        // Stop test if options panel is closed
        if (isTestingMic && optionsPanel != null && !optionsPanel.activeInHierarchy)
        {
            StopMicTest();
        }
    }

    private void OnDisable()
    {
        if (isTestingMic)
        {
            StopMicTest();
        }
    }
}
