using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class LevelRewardTracker : MonoBehaviour
{
    class PlayerRunStats
    {
        public PlayerStatus player;
        public float cleanedDirtFractions;
        public int knockouts;
        public int deaths;
        public bool transformedDeath;
    }

    public static LevelRewardTracker Instance { get; private set; }

    [Header("Rewards")]
    public int baseCompletionGerms = 10;
    public int personalCleaningGerms = 60;
    public int teamCleaningGerms = 40;
    public int timeBonusGerms = 35;
    public int knockoutPenaltyGerms = 8;
    public int deathPenaltyGerms = 20;
    [Range(0f, 1f)] public float transformedDeathMultiplier = 0.25f;

    [Header("Time")]
    public float targetCompletionSeconds = 480f;
    public float maximumRewardSeconds = 900f;

    [Header("Tracking")]
    public bool autoFindPlayers = true;
    public float playerRefreshInterval = 1f;

    [Header("Debug")]
    [SerializeField] private float runElapsedSeconds;
    [SerializeField] private bool rewardGranted;
    [SerializeField] private int lastAwardedGerms;
    [SerializeField] private float lastPersonalCleaningPercent;
    [SerializeField] private float lastTeamCleaningPercent;
    [SerializeField] private float lastTimePercent;

    private readonly Dictionary<int, PlayerRunStats> statsByPlayerKey =
        new Dictionary<int, PlayerRunStats>();
    private readonly HashSet<PlayerStatus> subscribedPlayers =
        new HashSet<PlayerStatus>();
    private LevelObjectiveManager objectiveManager;
    private float startTime;
    private float playerRefreshTimer;

    public int LastAwardedGerms => lastAwardedGerms;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        startTime = Time.time;
    }

    void OnEnable()
    {
        BindObjectiveManager();
        RefreshPlayers();
    }

    void OnDisable()
    {
        if (objectiveManager != null)
            objectiveManager.OnLevelCompleted -= HandleLevelCompleted;

        foreach (PlayerStatus player in subscribedPlayers)
            UnsubscribePlayer(player);

        subscribedPlayers.Clear();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        runElapsedSeconds = Time.time - startTime;

        if (!autoFindPlayers)
            return;

        playerRefreshTimer -= Time.deltaTime;
        if (playerRefreshTimer > 0f)
            return;

        playerRefreshTimer = Mathf.Max(0.1f, playerRefreshInterval);
        BindObjectiveManager();
        RefreshPlayers();
    }

    public static void RecordCleaning(PlayerStatus cleaner, float cleanedFraction)
    {
        if (cleaner == null || cleanedFraction <= 0f)
            return;

        if (Instance == null)
            return;

        Instance.RecordCleaningInternal(cleaner, cleanedFraction);
    }

    public static void RecordCleaningByClientId(
        ulong ownerClientId,
        float cleanedFraction)
    {
        if (cleanedFraction <= 0f || Instance == null)
            return;

        Instance.RecordCleaningByClientIdInternal(ownerClientId, cleanedFraction);
    }

    void RecordCleaningInternal(PlayerStatus cleaner, float cleanedFraction)
    {
        PlayerRunStats stats = GetStats(cleaner);
        stats.cleanedDirtFractions += Mathf.Max(0f, cleanedFraction);
    }

    void RecordCleaningByClientIdInternal(ulong ownerClientId, float cleanedFraction)
    {
        PlayerStatus player = FindPlayerByOwnerClientId(ownerClientId);
        if (player != null)
        {
            RecordCleaningInternal(player, cleanedFraction);
            return;
        }

        int key = unchecked((int)ownerClientId);
        if (!statsByPlayerKey.TryGetValue(key, out PlayerRunStats stats))
        {
            stats = new PlayerRunStats();
            statsByPlayerKey.Add(key, stats);
        }

        stats.cleanedDirtFractions += Mathf.Max(0f, cleanedFraction);
    }

    void BindObjectiveManager()
    {
        LevelObjectiveManager nextManager = LevelObjectiveManager.Instance;
        if (objectiveManager == nextManager)
            return;

        if (objectiveManager != null)
            objectiveManager.OnLevelCompleted -= HandleLevelCompleted;

        objectiveManager = nextManager;

        if (objectiveManager != null)
            objectiveManager.OnLevelCompleted += HandleLevelCompleted;
    }

    void RefreshPlayers()
    {
        PlayerStatus[] players =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        for (int i = 0; i < players.Length; i++)
            SubscribePlayer(players[i]);
    }

    void SubscribePlayer(PlayerStatus player)
    {
        if (player == null || subscribedPlayers.Contains(player))
            return;

        subscribedPlayers.Add(player);
        GetStats(player).player = player;
        player.OnKnockedOut += HandlePlayerKnockedOut;
        player.OnDeath += HandlePlayerDeath;
    }

    void UnsubscribePlayer(PlayerStatus player)
    {
        if (player == null)
            return;

        player.OnKnockedOut -= HandlePlayerKnockedOut;
        player.OnDeath -= HandlePlayerDeath;
    }

    void HandlePlayerKnockedOut(PlayerStatus player)
    {
        GetStats(player).knockouts++;
    }

    void HandlePlayerDeath(PlayerStatus player)
    {
        PlayerRunStats stats = GetStats(player);
        stats.deaths++;
        stats.transformedDeath |= player != null && player.IsTransformed();
    }

    void HandleLevelCompleted()
    {
        if (rewardGranted)
            return;

        rewardGranted = true;
        RefreshPlayers();
        AwardLocalPlayerReward();
    }

    void AwardLocalPlayerReward()
    {
        PlayerStatus player = FindRewardPlayer();
        PlayerRunStats stats = player != null ? GetStats(player) : new PlayerRunStats();
        int totalDirtCount = Mathf.Max(1, CountKnownDirtSpots());

        lastTeamCleaningPercent = objectiveManager != null
            ? objectiveManager.CurrentCleanPercent
            : 1f;
        lastPersonalCleaningPercent = Mathf.Clamp01(
            stats.cleanedDirtFractions / totalDirtCount);
        lastTimePercent = CalculateTimeRewardPercent();

        float reward =
            baseCompletionGerms +
            personalCleaningGerms * lastPersonalCleaningPercent +
            teamCleaningGerms * Mathf.Clamp01(lastTeamCleaningPercent) +
            timeBonusGerms * lastTimePercent -
            knockoutPenaltyGerms * stats.knockouts -
            deathPenaltyGerms * stats.deaths;

        if (stats.transformedDeath)
            reward *= transformedDeathMultiplier;

        lastAwardedGerms = Mathf.Max(0, Mathf.RoundToInt(reward));
        PlayerCurrencyState.SetLastRunReward(
            lastAwardedGerms,
            lastPersonalCleaningPercent,
            lastTeamCleaningPercent,
            lastTimePercent,
            stats.knockouts,
            stats.deaths,
            stats.transformedDeath);
        PlayerCurrencyState.AddGerms(lastAwardedGerms);

        Debug.Log(
            $"Awarded {lastAwardedGerms} germs. Total balance: {PlayerCurrencyState.Germs}.");
    }

    float CalculateTimeRewardPercent()
    {
        float target = Mathf.Max(1f, targetCompletionSeconds);
        float maximum = Mathf.Max(target + 1f, maximumRewardSeconds);

        if (runElapsedSeconds <= target)
            return 1f;

        return Mathf.Clamp01(1f - (runElapsedSeconds - target) / (maximum - target));
    }

    PlayerStatus FindRewardPlayer()
    {
        PlayerStatus[] players =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        if (players.Length == 0)
            return null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            for (int i = 0; i < players.Length; i++)
            {
                PlayerStatus player = players[i];
                if (player != null && player.IsOwner)
                    return player;
            }
        }

        return players[0];
    }

    PlayerStatus FindPlayerByOwnerClientId(ulong ownerClientId)
    {
        PlayerStatus[] players =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus player = players[i];
            if (player == null ||
                player.NetworkObject == null ||
                !player.NetworkObject.IsSpawned)
            {
                continue;
            }

            if (player.NetworkObject.OwnerClientId == ownerClientId)
                return player;
        }

        return null;
    }

    int CountKnownDirtSpots()
    {
        DirtSpot[] dirtSpots =
            FindObjectsByType<DirtSpot>(FindObjectsInactive.Include);
        return Mathf.Max(1, dirtSpots.Length);
    }

    PlayerRunStats GetStats(PlayerStatus player)
    {
        int key = GetPlayerKey(player);
        if (!statsByPlayerKey.TryGetValue(key, out PlayerRunStats stats))
        {
            stats = new PlayerRunStats { player = player };
            statsByPlayerKey.Add(key, stats);
        }

        return stats;
    }

    int GetPlayerKey(PlayerStatus player)
    {
        if (player == null)
            return 0;

        if (player.NetworkObject != null && player.NetworkObject.IsSpawned)
            return unchecked((int)player.NetworkObject.OwnerClientId);

        return player.GetInstanceID();
    }
}
