using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class ElectricPoolCable : PoolWaterReactive
{
    [Header("Cable")]
    [SerializeField, Min(1f)] private float waterNeededToDisable = 12f;
    [SerializeField] private bool onlyDisableWhenPowered = true;
    [SerializeField] private bool chemicalWaterDisablesFaster = true;

    [Header("Shock")]
    [SerializeField, Min(0f)] private float shockDamagePerSecond = 18f;
    [SerializeField, Min(0.05f)] private float shockTickInterval = 0.35f;
    [SerializeField] private bool emitNoiseWhenShocking = true;
    [SerializeField, Min(0f)] private float shockNoiseRadius = 14f;

    [Header("Visuals")]
    [SerializeField] private GameObject litVisualRoot;
    [SerializeField] private GameObject disabledVisualRoot;
    [SerializeField] private Light cableLight;
    [SerializeField] private Renderer[] tintRenderers = new Renderer[0];
    [SerializeField] private Color litColor = Color.yellow;
    [SerializeField] private Color unlitColor = Color.black;

    private readonly Dictionary<PlayerStatus, float> nextShockTimes =
        new Dictionary<PlayerStatus, float>();

    private ElectricSwimmingPoolMechanic pool;
    private float wetness;
    private bool powered;
    private bool disabled;

    private void OnTriggerStay(Collider other)
    {
        if (!powered || disabled || other == null)
            return;

        PlayerStatus player = other.GetComponentInParent<PlayerStatus>();
        if (player == null || !player.CanAct())
            return;

        float nextTime;
        nextShockTimes.TryGetValue(player, out nextTime);
        if (Time.time < nextTime)
            return;

        player.TakeDamage(shockDamagePerSecond * shockTickInterval);
        nextShockTimes[player] = Time.time + shockTickInterval;

        if (emitNoiseWhenShocking)
            NoiseEvent.Emit(transform.position, shockNoiseRadius, gameObject);
    }

    public void BindPool(ElectricSwimmingPoolMechanic owningPool)
    {
        pool = owningPool;
    }

    public void SetPowered(bool value)
    {
        powered = value;
        RefreshVisuals();
    }

    public override void ApplyPoolWaterHit(
        WaterQuality waterQuality,
        float waterPower,
        Vector3 sourcePosition)
    {
        if (disabled || waterPower <= 0f)
            return;
        if (onlyDisableWhenPowered && !powered)
            return;
        if (waterQuality == WaterQuality.Contaminated)
            return;

        float multiplier = waterQuality == WaterQuality.ChemicallyEnhanced &&
            chemicalWaterDisablesFaster
            ? 1.5f
            : 1f;

        wetness += waterPower * multiplier;
        if (wetness >= waterNeededToDisable)
            DisableCable();
    }

    private void DisableCable()
    {
        if (disabled)
            return;

        disabled = true;
        pool?.NotifyCableDisabled(this);
        RefreshVisuals();

        NetworkObject networkObject = GetComponent<NetworkObject>();
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null &&
            networkManager.IsListening &&
            networkObject != null &&
            networkObject.IsSpawned)
        {
            if (networkManager.IsServer)
                networkObject.Despawn(true);
            return;
        }

        Destroy(gameObject, 0.25f);
    }

    private void RefreshVisuals()
    {
        bool lit = powered && !disabled;

        if (litVisualRoot != null)
            litVisualRoot.SetActive(lit);
        if (disabledVisualRoot != null)
            disabledVisualRoot.SetActive(disabled);
        if (cableLight != null)
            cableLight.enabled = lit;

        ApplyTint(lit ? litColor : unlitColor);
    }

    private void ApplyTint(Color color)
    {
        if (tintRenderers == null || tintRenderers.Length == 0)
            tintRenderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < tintRenderers.Length; i++)
        {
            Renderer target = tintRenderers[i];
            if (target == null)
                continue;

            Material material = target.material;
            if (material == null)
                continue;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }
    }
}
