using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class CameraWobble : MonoBehaviour
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
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        float mouseSpeed = mouseDelta.magnitude;
        float targetAmplitude = mouseSpeed > 0.05f ? wobbleAmplitude : 0f;

        currentAmplitude = Mathf.Lerp(currentAmplitude, targetAmplitude, Time.deltaTime * smoothSpeed);

        noise.AmplitudeGain = currentAmplitude;
        noise.FrequencyGain = wobbleFrequency;
    }
}