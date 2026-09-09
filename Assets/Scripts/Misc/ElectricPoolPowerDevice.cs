using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

[DisallowMultipleComponent]
public class ElectricPoolPowerDevice : PoolWaterReactive, IPlayerInteractable
{
    [Header("Device")]
    [SerializeField, Min(1f)] private float waterNeededToDisable = 25f;
    [SerializeField] private bool cleanWaterDisables = true;
    [SerializeField] private bool chemicalWaterDisablesFaster = true;

    [Header("Visuals")]
    [SerializeField] private GameObject poweredVisualRoot;
    [SerializeField] private GameObject disabledVisualRoot;
    [SerializeField] private Light poweredLight;
    [SerializeField] private Renderer[] tintRenderers = new Renderer[0];
    [SerializeField] private Color poweredColor = Color.yellow;
    [SerializeField] private Color disabledColor = Color.black;

    private ElectricSwimmingPoolMechanic pool;
    private float wetness;
    private bool powered;
    private NetworkManager registeredManager;
    private string messageKey;
    private float nextPublishTime;

    void Update()
    {
        var manager = NetworkManager.Singleton;
        var obj = GetComponent<NetworkObject>();
        if (manager == null || !manager.IsListening || obj == null || !obj.IsSpawned) return;
        if (registeredManager == null)
        {
            registeredManager = manager;
            messageKey = "ElectricPower/" + obj.NetworkObjectId;
            manager.CustomMessagingManager.RegisterNamedMessageHandler(messageKey, HandlePowerMessage);
        }
        if (!manager.IsServer || Time.unscaledTime < nextPublishTime || pool == null) return;
        nextPublishTime = Time.unscaledTime + 0.25f;
        using (var writer = new FastBufferWriter(16, Allocator.Temp))
        {
            writer.WriteValueSafe(powered);
            writer.WriteValueSafe(pool.PoolSyncId);
            writer.WriteValueSafe(pool.PowerReturnSeconds);
            foreach (ulong clientId in manager.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId)
                    manager.CustomMessagingManager.SendNamedMessage(messageKey, clientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    void HandlePowerMessage(ulong sender, FastBufferReader reader)
    {
        if (registeredManager == null || !registeredManager.IsListening) return;
        if (registeredManager.IsServer)
        {
            if (!registeredManager.ConnectedClients.TryGetValue(sender, out var client) || client.PlayerObject == null) return;
            var inventory = client.PlayerObject.GetComponent<PlayerInventory>();
            var status = client.PlayerObject.GetComponent<PlayerStatus>();
            if (inventory == null || status == null || !status.CanAct()) return;
            if (Vector3.Distance(client.PlayerObject.transform.position, transform.position) > inventory.pickupRange + 1f) return;
            Interact(inventory);
            nextPublishTime = 0f;
            return;
        }
        if (sender != NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out bool state);
        reader.ReadValueSafe(out int poolId);
        reader.ReadValueSafe(out float remaining);
        SetPowered(state);
        if (pool == null)
            foreach (var candidate in FindObjectsByType<ElectricSwimmingPoolMechanic>(FindObjectsInactive.Include))
            {
                if (candidate.PoolSyncId == poolId) { pool = candidate; break; }
            }
        if (pool != null) pool.ApplyRemotePowerState(state, remaining);
    }

    void OnDestroy()
    {
        if (registeredManager != null && registeredManager.CustomMessagingManager != null && messageKey != null)
            registeredManager.CustomMessagingManager.UnregisterNamedMessageHandler(messageKey);
    }

    public void BindPool(ElectricSwimmingPoolMechanic owningPool)
    {
        pool = owningPool;
    }

    public void SetPowered(bool value)
    {
        powered = value;
        if (powered)
            wetness = 0f;

        if (poweredVisualRoot != null)
            poweredVisualRoot.SetActive(powered);
        if (disabledVisualRoot != null)
            disabledVisualRoot.SetActive(!powered);
        if (poweredLight != null)
            poweredLight.enabled = powered;

        // ApplyTint();
    }

    public void Interact(PlayerInventory inventory)
    {
        if (registeredManager != null && registeredManager.IsListening && !registeredManager.IsServer)
        {
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
            {
                writer.WriteValueSafe((byte)0);
                registeredManager.CustomMessagingManager.SendNamedMessage(messageKey, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            }
            return;
        }
        if (!powered || pool == null || !pool.CanDisablePower())
            return;

        pool.DisablePowerTemporarily();
    }

    public override void ApplyPoolWaterHit(
        WaterQuality waterQuality,
        float waterPower,
        Vector3 sourcePosition)
    {
        if (!powered || waterPower <= 0f)
            return;
        if (pool != null && !pool.CanDisablePower())
            return;
        if (waterQuality == WaterQuality.Clean && !cleanWaterDisables)
            return;
        if (waterQuality == WaterQuality.Contaminated)
            return;

        float multiplier = waterQuality == WaterQuality.ChemicallyEnhanced &&
            chemicalWaterDisablesFaster
            ? 1.5f
            : 1f;

        wetness += waterPower * multiplier;
        if (wetness >= waterNeededToDisable)
            pool?.DisablePowerTemporarily();
    }

    private void ApplyTint()
    {
        if (tintRenderers == null || tintRenderers.Length == 0)
            tintRenderers = GetComponentsInChildren<Renderer>(true);

        Color color = powered ? poweredColor : disabledColor;
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
