using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// Runs only on the host (or offline). PlayerStatus replicates the recovery.
[DisallowMultipleComponent]
public class AdaptiveWaterAssistance : MonoBehaviour
{
    public float stalledSeconds = 90f;
    public float cooldownSeconds = 120f;
    public int recoveriesPerLevel = 3;
    [Range(0f, 1f)] public float lowWaterThreshold = 0.2f;
    [Range(0f, 1f)] public float recoveryFraction = 0.25f;

    private readonly HashSet<PlayerStatus> observed = new HashSet<PlayerStatus>();
    private LevelObjectiveManager objectives;
    private float nextCheck, lastProgressTime, nextRecovery, bestProgress;
    private int recoveries;
    private float recentKnockoutTime = float.NegativeInfinity;

    void Start()
    {
        objectives = GetComponent<LevelObjectiveManager>();
        lastProgressTime = Time.time;
        nextRecovery = Time.time + stalledSeconds;
    }

    void Update()
    {
        var network = NetworkManager.Singleton;
        if (network != null && network.IsListening && !network.IsServer) return;
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 2f;
        if (objectives == null || !objectives.WaterValveActivated || objectives.LevelCompleted) return;

        var players = FindObjectsByType<PlayerStatus>(FindObjectsSortMode.None);
        foreach (var player in players)
            if (observed.Add(player)) player.OnKnockedOut += OnKnockout;

        float progress = objectives.CurrentCleanPercent;
        if (progress >= bestProgress + 0.01f)
        {
            bestProgress = progress;
            lastProgressTime = Time.time;
        }

        if (recoveries >= recoveriesPerLevel || Time.time < nextRecovery) return;
        bool stalled = Time.time - lastProgressTime >= stalledSeconds;
        bool struggling = Time.time - recentKnockoutTime <= 60f;
        if (!stalled && !struggling) return;

        // Give one small recovery to the most depleted active player, never a
        // full team refill. Clean nearby sources count as available supplies.
        PlayerStatus recipient = null;
        float lowest = lowWaterThreshold;
        foreach (var player in players)
        {
            if (!player.CanAct() || player.IsKnockedOut()) continue;
            if (player.GetWaterPercent() > lowWaterThreshold) continue;
            float usable = player.HasContaminatedWater() ? 0f : player.GetWaterPercent();
            if (usable <= lowest) { lowest = usable; recipient = player; }
        }
        if (recipient == null) return;
        foreach (var source in FindObjectsByType<WaterSourceDryable>(FindObjectsSortMode.None))
            if (source.HasWater && source.waterQuality != WaterQuality.Contaminated &&
                source.currentWaterAmount >= recipient.maxWater * recoveryFraction &&
                (source.transform.position - recipient.transform.position).sqrMagnitude < 144f)
                return;

        // A nearly empty contaminated tank can be purified without converting
        // a full tank into an unlimited free filter.
        if (recipient.GetWaterPercent() > lowWaterThreshold) return;
        bool added = recipient.AddWaterIgnoringControlLock(
            recipient.maxWater * recoveryFraction, WaterQuality.Clean, true);
        if (!added) return;
        recoveries++;
        nextRecovery = Time.time + cooldownSeconds;
        lastProgressTime = Time.time;
    }

    void OnKnockout(PlayerStatus player) { recentKnockoutTime = Time.time; }

    void OnDestroy()
    {
        foreach (var player in observed)
            if (player != null) player.OnKnockedOut -= OnKnockout;
    }
}
