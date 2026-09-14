using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using Unity.Netcode;

public class CameraWobble : NetworkBehaviour
{
    public CinemachineCamera vcam;
    public float wobbleAmplitude = 0.5f;
    public float wobbleFrequency = 1f;
    public float smoothSpeed = 5f;

    private CinemachineBasicMultiChannelPerlin noise;
    private float currentAmplitude;

    void Start()
    {
        noise = vcam.GetComponent<CinemachineBasicMultiChannelPerlin>();
    }

    void Update()
    {
        if (!IsOwner) return;
        Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        if (Gamepad.current != null) mouseDelta += Gamepad.current.rightStick.ReadValue();
        if (noise == null) return;
        if (GameSettingsManager.PhotosensitiveMode || PauseMenuController.ReduceCameraShake) { noise.AmplitudeGain = 0f; return; }

        float mouseSpeed = mouseDelta.magnitude;
        float targetAmplitude = mouseSpeed > 0.05f ? wobbleAmplitude : 0f;

        currentAmplitude = Mathf.Lerp(currentAmplitude, targetAmplitude, Time.deltaTime * smoothSpeed);

        noise.AmplitudeGain = currentAmplitude;
        noise.FrequencyGain = wobbleFrequency;
    }
}