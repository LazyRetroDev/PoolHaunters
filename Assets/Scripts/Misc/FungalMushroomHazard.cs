using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class FungalMushroomHazard : PoolWaterReactive
{
    [Header("Mushroom")]
    [SerializeField] private bool goodFungus;
    [SerializeField, Min(1f)] private float health = 20f;
    [SerializeField, Range(0.05f, 1f)] private float goodFungusCleanPortion = 0.35f;
    [SerializeField] private bool cleanWaterRemoves = true;
    [SerializeField] private bool chemicalWaterRemovesFaster = true;
    [SerializeField] private bool contaminatedWaterHeals = true;
    [SerializeField] private GameObject removedVisualRoot;
    [SerializeField] private Renderer[] tintRenderers = new Renderer[0];
    [SerializeField] private Color goodColor = Color.yellow;
    [SerializeField] private Color harmfulColor = new Color(0.45f, 0.1f, 0.55f, 1f);

    [Header("Spore Cloud / Infection")]
    [SerializeField] private bool releaseSporesWhenSteppedOn = true;
    [SerializeField, Min(0.1f)] private float sporeCloudRadius = 2.5f;
    [SerializeField, Min(0f)] private float sporeCloudDuration = 5f;
    [SerializeField, Min(0f)] private float infectionDamagePerSecond = 3f;
    [SerializeField, Min(0f)] private float infectionDuration = 8f;
    [SerializeField, Min(0.05f)] private float infectionTickInterval = 0.5f;
    [SerializeField] private LayerMask playerLayers = ~0;
    [SerializeField] private ParticleSystem sporeCloudParticles;

    private readonly Dictionary<PlayerStatus, float> infectionTimers =
        new Dictionary<PlayerStatus, float>();
    private readonly Dictionary<PlayerStatus, float> nextInfectionTicks =
        new Dictionary<PlayerStatus, float>();

    private FungalSwimmingPoolMechanic owningPool;
    private float currentHealth;
    private bool removed;
    private bool sporeCloudActive;
    private float sporeCloudTimer;
    private bool waitingForInfectionsToFinish;

    public bool IsGoodFungus => goodFungus;

    private void Awake()
    {
        currentHealth = Mathf.Max(1f, health);
        ApplyTint();
    }

    private void Update()
    {
        UpdateSporeCloud();
        TickInfections();

        if (waitingForInfectionsToFinish &&
            infectionTimers.Count == 0 &&
            !sporeCloudActive)
        {
            DestroyRemovedObject();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!releaseSporesWhenSteppedOn || removed || goodFungus)
            return;

        PlayerStatus player = other != null
            ? other.GetComponentInParent<PlayerStatus>()
            : null;
        if (player == null || !player.CanAct())
            return;

        StartSporeCloud();
    }

    public void BindPool(FungalSwimmingPoolMechanic pool)
    {
        owningPool = pool;
    }

    public void SetGoodFungus(bool value)
    {
        goodFungus = value;
        ApplyTint();
    }

    public void SetGoodFungusCleanPortion(float portion)
    {
        goodFungusCleanPortion = Mathf.Clamp01(portion);
    }

    public void RemoveByHelpfulFungus()
    {
        RemoveMushroom();
    }

    public override void ApplyPoolWaterHit(
        WaterQuality waterQuality,
        float waterPower,
        Vector3 sourcePosition)
    {
        if (IsSpawned && !IsServer && IsNetworkSessionRunning())
        {
            ApplyPoolWaterHitServerRpc((int)waterQuality, waterPower, sourcePosition);
            return;
        }

        ApplyPoolWaterHitLocal(waterQuality, waterPower, sourcePosition);

        if (IsSpawned && IsServer && IsNetworkSessionRunning())
            ApplyPoolWaterHitClientRpc((int)waterQuality, waterPower, sourcePosition);
    }

    void ApplyPoolWaterHitLocal(
        WaterQuality waterQuality,
        float waterPower,
        Vector3 sourcePosition)
    {
        if (removed || waterPower <= 0f)
            return;

        if (waterQuality == WaterQuality.Contaminated && contaminatedWaterHeals)
        {
            currentHealth = Mathf.Min(health, currentHealth + waterPower);
            return;
        }

        if (waterQuality == WaterQuality.Clean && !cleanWaterRemoves)
            return;

        float multiplier = waterQuality == WaterQuality.ChemicallyEnhanced &&
            chemicalWaterRemovesFaster
            ? 1.75f
            : 1f;

        currentHealth -= waterPower * multiplier;
        if (currentHealth <= 0f)
        {
            if (goodFungus)
                owningPool?.RemoveFungusPortion(goodFungusCleanPortion, this);
            else
                RemoveMushroom();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void ApplyPoolWaterHitServerRpc(
        int waterQuality,
        float waterPower,
        Vector3 sourcePosition)
    {
        ApplyPoolWaterHitLocal((WaterQuality)waterQuality, waterPower, sourcePosition);
        ApplyPoolWaterHitClientRpc(waterQuality, waterPower, sourcePosition);
    }

    [ClientRpc]
    void ApplyPoolWaterHitClientRpc(
        int waterQuality,
        float waterPower,
        Vector3 sourcePosition)
    {
        if (IsServer)
            return;

        ApplyPoolWaterHitLocal((WaterQuality)waterQuality, waterPower, sourcePosition);
    }

    static bool IsNetworkSessionRunning()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening;
    }

    private void StartSporeCloud()
    {
        if (removed || sporeCloudActive)
            return;

        sporeCloudActive = true;
        sporeCloudTimer = Mathf.Max(0.1f, sporeCloudDuration);

        if (sporeCloudParticles != null)
            sporeCloudParticles.Play();

        ApplyCloudInfection();
    }

    private void UpdateSporeCloud()
    {
        if (!sporeCloudActive)
            return;

        sporeCloudTimer -= Time.deltaTime;
        ApplyCloudInfection();

        if (sporeCloudTimer > 0f)
            return;

        sporeCloudActive = false;
        RemoveMushroom(waitForInfection: infectionTimers.Count > 0);
    }

    private void ApplyCloudInfection()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            sporeCloudRadius,
            playerLayers,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            PlayerStatus player = hits[i] != null
                ? hits[i].GetComponentInParent<PlayerStatus>()
                : null;
            if (player == null)
                continue;

            infectionTimers[player] = infectionDuration;
            nextInfectionTicks[player] = Time.time;
        }
    }

    private void TickInfections()
    {
        if (infectionTimers.Count == 0)
            return;

        List<PlayerStatus> finished = null;
        List<PlayerStatus> players = new List<PlayerStatus>(infectionTimers.Keys);
        for (int i = 0; i < players.Count; i++)
        {
            PlayerStatus player = players[i];
            if (player == null || !infectionTimers.ContainsKey(player))
                continue;

            float remaining = infectionTimers[player] - Time.deltaTime;

            if (player == null || remaining <= 0f || !player.CanAct())
            {
                if (finished == null)
                    finished = new List<PlayerStatus>();
                finished.Add(player);
                continue;
            }

            infectionTimers[player] = remaining;
            float nextTick;
            nextInfectionTicks.TryGetValue(player, out nextTick);
            if (Time.time < nextTick)
                continue;

            player.TakeDamage(infectionDamagePerSecond * infectionTickInterval);
            nextInfectionTicks[player] = Time.time + infectionTickInterval;
        }

        if (finished == null)
            return;

        for (int i = 0; i < finished.Count; i++)
        {
            if (object.ReferenceEquals(finished[i], null))
                continue;

            infectionTimers.Remove(finished[i]);
            nextInfectionTicks.Remove(finished[i]);
        }
    }

    private void RemoveMushroom(bool waitForInfection = false)
    {
        if (removed)
            return;

        removed = true;
        owningPool?.NotifyMushroomRemoved(this);

        if (removedVisualRoot != null)
            removedVisualRoot.SetActive(false);

        Collider[] colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        if (waitForInfection && infectionTimers.Count > 0)
        {
            waitingForInfectionsToFinish = true;
            return;
        }

        DestroyRemovedObject();
    }

    private void DestroyRemovedObject()
    {
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

        Destroy(gameObject);
    }

    private void ApplyTint()
    {
        if (tintRenderers == null || tintRenderers.Length == 0)
            tintRenderers = GetComponentsInChildren<Renderer>(true);

        Color color = goodFungus ? goodColor : harmfulColor;
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
