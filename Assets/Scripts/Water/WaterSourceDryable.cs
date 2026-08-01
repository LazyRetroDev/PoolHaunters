using Unity.Netcode;
using UnityEngine;

public class WaterSourceDryable : NetworkBehaviour
{
    [Header("State")]
    public bool startsDry = false;
    public bool isDry;

    [Header("Water Supply")]
    public WaterQuality waterQuality = WaterQuality.Clean;
    public float maxWaterAmount = 250f;
    public float currentWaterAmount = 250f;
    public bool refillToMaxOnStart = true;
    public bool replacePlayerWaterQuality = false;

    [Header("Deterioration")]
    public bool deterioratesOverTime = true;
    public float waterLossPerSecond = 0.25f;
    public bool canBecomeContaminated = true;
    public float contaminationDelay = 120f;

    [Header("Visuals")]
    public GameObject wetVisualRoot;
    public GameObject dryVisualRoot;
    public ParticleSystem[] waterParticles;
    public Renderer[] renderersToDisableWhenDry;
    public Collider[] collidersToDisableWhenDry;

    [Header("Optional")]
    public bool disableObjectWhenDry = false;
    public GameObject[] extraObjectsToDisableWhenDry;

    private readonly NetworkVariable<bool> syncedIsDry =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> syncedWaterAmount =
        new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> syncedWaterQuality =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private float contaminationTimer;
    private bool applyingNetworkState;

    public float WaterPercent => maxWaterAmount > 0f ? currentWaterAmount / maxWaterAmount : 0f;
    public bool HasWater => !isDry && currentWaterAmount > 0f;

    void Start()
    {
        if (IsSpawned && !IsServer)
            return;

        if (refillToMaxOnStart)
            currentWaterAmount = maxWaterAmount;

        currentWaterAmount = Mathf.Clamp(currentWaterAmount, 0f, maxWaterAmount);
        contaminationTimer = contaminationDelay;
        isDry = startsDry || currentWaterAmount <= 0f;
        UpdateDryState();
        SyncNetworkState();
    }

    public override void OnNetworkSpawn()
    {
        syncedIsDry.OnValueChanged += HandleSyncedIsDryChanged;
        syncedWaterAmount.OnValueChanged += HandleSyncedWaterAmountChanged;
        syncedWaterQuality.OnValueChanged += HandleSyncedWaterQualityChanged;

        if (IsServer)
            SyncNetworkState();

        ApplyNetworkState();
    }

    public override void OnNetworkDespawn()
    {
        syncedIsDry.OnValueChanged -= HandleSyncedIsDryChanged;
        syncedWaterAmount.OnValueChanged -= HandleSyncedWaterAmountChanged;
        syncedWaterQuality.OnValueChanged -= HandleSyncedWaterQualityChanged;
    }

    void Update()
    {
        if (isDry) return;
        if (IsNetworkClientReplica()) return;

        UpdateDeterioration();
        UpdateContaminationTimer();
    }

    void UpdateDeterioration()
    {
        if (!deterioratesOverTime || waterLossPerSecond <= 0f) return;
        DrainWater(waterLossPerSecond * Time.deltaTime, out _);
    }

    void UpdateContaminationTimer()
    {
        if (!canBecomeContaminated || contaminationDelay <= 0f) return;
        if (waterQuality == WaterQuality.Contaminated) return;

        contaminationTimer -= Time.deltaTime;
        if (contaminationTimer <= 0f)
            Contaminate();
    }

    public float DrainWater(float requestedAmount, out WaterQuality drainedQuality)
    {
        drainedQuality = waterQuality;
        if (isDry || requestedAmount <= 0f || currentWaterAmount <= 0f) return 0f;

        float drainedAmount = Mathf.Min(requestedAmount, currentWaterAmount);

        if (IsNetworkClientReplica())
        {
            ApplyDrainWaterLocal(drainedAmount);
            DrainWaterServerRpc(requestedAmount);
            return drainedAmount;
        }

        ApplyDrainWaterLocal(drainedAmount);
        SyncNetworkState();
        return drainedAmount;
    }

    void ApplyDrainWaterLocal(float drainedAmount)
    {
        if (drainedAmount <= 0f)
            return;

        currentWaterAmount -= drainedAmount;

        if (currentWaterAmount <= 0f)
        {
            currentWaterAmount = 0f;
            DryOut();
        }
    }

    public void Contaminate()
    {
        if (isDry) return;

        if (IsNetworkClientReplica())
        {
            ApplyQualityLocal(WaterQuality.Contaminated);
            ContaminateServerRpc();
            return;
        }

        ApplyQualityLocal(WaterQuality.Contaminated);
        SyncNetworkState();
    }

    void ApplyQualityLocal(WaterQuality quality)
    {
        waterQuality = quality;
    }

    public void SetQuality(WaterQuality quality)
    {
        if (IsNetworkClientReplica())
        {
            ApplyQualityLocal(quality);
            SetQualityServerRpc((int)quality);
            return;
        }

        ApplyQualityLocal(quality);
        SyncNetworkState();
    }

    public void DryOut()
    {
        if (isDry) return;

        if (IsNetworkClientReplica())
        {
            ApplyDryStateLocal(true, 0f);
            DryOutServerRpc();
            return;
        }

        ApplyDryStateLocal(true, 0f);
        SyncNetworkState();
    }

    public void RestoreWater()
    {
        if (!isDry && currentWaterAmount > 0f) return;

        float restoredAmount = currentWaterAmount <= 0f
            ? maxWaterAmount
            : currentWaterAmount;

        if (IsNetworkClientReplica())
        {
            ApplyDryStateLocal(false, restoredAmount);
            RestoreWaterServerRpc();
            return;
        }

        ApplyDryStateLocal(false, restoredAmount);
        SyncNetworkState();
    }

    void ApplyDryStateLocal(bool dry, float amount)
    {
        isDry = dry;
        currentWaterAmount = dry ? 0f : Mathf.Clamp(amount, 0f, maxWaterAmount);
        contaminationTimer = contaminationDelay;
        UpdateDryState();
    }

    void SyncNetworkState()
    {
        if (!IsSpawned || !IsServer || applyingNetworkState)
            return;

        syncedIsDry.Value = isDry;
        syncedWaterAmount.Value = currentWaterAmount;
        syncedWaterQuality.Value = (int)waterQuality;
    }

    void ApplyNetworkState()
    {
        if (!IsSpawned)
            return;

        applyingNetworkState = true;

        try
        {
            isDry = syncedIsDry.Value;
            currentWaterAmount = Mathf.Clamp(
                syncedWaterAmount.Value,
                0f,
                maxWaterAmount);
            waterQuality = (WaterQuality)syncedWaterQuality.Value;
            contaminationTimer = contaminationDelay;
            UpdateDryState();
        }
        finally
        {
            applyingNetworkState = false;
        }
    }

    void HandleSyncedIsDryChanged(bool previousValue, bool newValue)
    {
        ApplyNetworkState();
    }

    void HandleSyncedWaterAmountChanged(float previousValue, float newValue)
    {
        ApplyNetworkState();
    }

    void HandleSyncedWaterQualityChanged(int previousValue, int newValue)
    {
        ApplyNetworkState();
    }

    bool IsNetworkClientReplica()
    {
        return IsSpawned &&
            !IsServer &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void DrainWaterServerRpc(float requestedAmount)
    {
        if (requestedAmount <= 0f || isDry || currentWaterAmount <= 0f)
            return;

        float drainedAmount = Mathf.Min(requestedAmount, currentWaterAmount);
        ApplyDrainWaterLocal(drainedAmount);
        SyncNetworkState();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ContaminateServerRpc()
    {
        Contaminate();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void SetQualityServerRpc(int quality)
    {
        SetQuality((WaterQuality)quality);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void DryOutServerRpc()
    {
        DryOut();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void RestoreWaterServerRpc()
    {
        RestoreWater();
    }

    void UpdateDryState()
    {
        if (disableObjectWhenDry && !isDry && !gameObject.activeSelf)
            gameObject.SetActive(true);

        if (wetVisualRoot != null)
            wetVisualRoot.SetActive(!isDry);

        if (dryVisualRoot != null)
            dryVisualRoot.SetActive(isDry);

        for (int i = 0; i < waterParticles.Length; i++)
        {
            if (waterParticles[i] == null) continue;

            if (isDry)
                waterParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            else
                waterParticles[i].Play();
        }

        for (int i = 0; i < renderersToDisableWhenDry.Length; i++)
        {
            if (renderersToDisableWhenDry[i] != null)
                renderersToDisableWhenDry[i].enabled = !isDry;
        }

        for (int i = 0; i < collidersToDisableWhenDry.Length; i++)
        {
            if (collidersToDisableWhenDry[i] != null)
                collidersToDisableWhenDry[i].enabled = !isDry;
        }

        for (int i = 0; i < extraObjectsToDisableWhenDry.Length; i++)
        {
            if (extraObjectsToDisableWhenDry[i] != null)
                extraObjectsToDisableWhenDry[i].SetActive(!isDry);
        }

        if (disableObjectWhenDry && isDry)
            gameObject.SetActive(false);
    }
}
