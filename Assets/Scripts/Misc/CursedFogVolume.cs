using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class CursedFogVolume : NetworkBehaviour
{
    private static readonly HashSet<CursedFogVolume> ActiveVolumes =
        new HashSet<CursedFogVolume>();

    [SerializeField, Min(0f)] private float visualEnableDistance = 35f;
    [SerializeField, Min(0f)] private float visualDisableDistance = 45f;

    private readonly NetworkVariable<bool> fadingNetworkState = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private ParticleSystem[] particleSystems;
    private ParticleSystemRenderer[] particleRenderers;
    private bool[] rendererInitialStates;
    private bool visualsActive;
    private bool fading;

    private void Awake()
    {
        particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        particleRenderers = GetComponentsInChildren<ParticleSystemRenderer>(true);
        rendererInitialStates = new bool[particleRenderers.Length];

        for (int i = 0; i < particleRenderers.Length; i++)
            rendererInitialStates[i] = particleRenderers[i].enabled;

        visualsActive = true;
        SetVisualsActive(false);
    }

    private void OnEnable()
    {
        ActiveVolumes.Add(this);
        CursedFogVisualCullingRunner.Register(this);
    }

    private void OnDisable()
    {
        ActiveVolumes.Remove(this);
        CursedFogVisualCullingRunner.Unregister(this);
    }

    public override void OnNetworkSpawn()
    {
        fadingNetworkState.OnValueChanged += HandleFadingStateChanged;
        if (fadingNetworkState.Value)
            BeginFadeVisuals();
    }

    public override void OnNetworkDespawn()
    {
        fadingNetworkState.OnValueChanged -= HandleFadingStateChanged;
    }

    internal static float BeginFadeForAll()
    {
        float longestLifetime = 0f;
        foreach (CursedFogVolume volume in ActiveVolumes)
        {
            if (volume != null)
                longestLifetime = Mathf.Max(longestLifetime, volume.BeginFade());
        }

        return longestLifetime;
    }

    private float BeginFade()
    {
        if (IsSpawned && IsServer && !fadingNetworkState.Value)
            fadingNetworkState.Value = true;

        return BeginFadeVisuals();
    }

    private float BeginFadeVisuals()
    {
        fading = true;
        float longestLifetime = 0f;
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (particleSystem == null)
                continue;

            longestLifetime = Mathf.Max(
                longestLifetime,
                particleSystem.main.startLifetime.constantMax);
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        return longestLifetime;
    }

    private void HandleFadingStateChanged(bool previousValue, bool newValue)
    {
        if (newValue)
            BeginFadeVisuals();
    }

    internal void UpdateVisualCulling(Transform localPlayer)
    {
        if (localPlayer == null)
        {
            SetVisualsActive(false);
            return;
        }

        float threshold = visualsActive ? visualDisableDistance : visualEnableDistance;
        bool shouldBeActive =
            (transform.position - localPlayer.position).sqrMagnitude <= threshold * threshold;
        SetVisualsActive(shouldBeActive);
    }

    private void SetVisualsActive(bool active)
    {
        if (visualsActive == active)
            return;

        visualsActive = active;
        for (int i = 0; i < particleRenderers.Length; i++)
        {
            if (particleRenderers[i] != null)
                particleRenderers[i].enabled = active && rendererInitialStates[i];
        }

        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i] == null)
                continue;

            if (active)
            {
                particleSystems[i].Play(true);
                if (fading)
                    particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            else
                particleSystems[i].Pause(true);
        }
    }
}

internal sealed class CursedFogVisualCullingRunner : MonoBehaviour
{
    private const float CheckInterval = 0.25f;
    private static readonly HashSet<CursedFogVolume> Volumes = new HashSet<CursedFogVolume>();
    private static CursedFogVisualCullingRunner instance;

    private float nextCheckTime;
    private float nextPlayerLookupTime;
    private Transform localPlayer;

    internal static void Register(CursedFogVolume volume)
    {
        Volumes.Add(volume);
        EnsureRunner();
    }

    internal static void Unregister(CursedFogVolume volume)
    {
        Volumes.Remove(volume);
        if (Volumes.Count == 0 && instance != null)
        {
            Destroy(instance.gameObject);
            instance = null;
        }
    }

    private static void EnsureRunner()
    {
        if (instance != null)
            return;

        GameObject runnerObject = new GameObject("Cursed Fog Visual Culling");
        DontDestroyOnLoad(runnerObject);
        instance = runnerObject.AddComponent<CursedFogVisualCullingRunner>();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheckTime)
            return;

        nextCheckTime = Time.unscaledTime + CheckInterval;
        if (Time.unscaledTime >= nextPlayerLookupTime)
        {
            nextPlayerLookupTime = Time.unscaledTime + 1f;
            localPlayer = FindLocalPlayer();
        }

        foreach (CursedFogVolume volume in Volumes)
        {
            if (volume != null)
                volume.UpdateVisualCulling(localPlayer);
        }
    }

    private static Transform FindLocalPlayer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening &&
            networkManager.SpawnManager != null)
        {
            NetworkObject playerObject = networkManager.SpawnManager.GetLocalPlayerObject();
            return playerObject != null ? playerObject.transform : null;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        return player != null ? player.transform : null;
    }
}
