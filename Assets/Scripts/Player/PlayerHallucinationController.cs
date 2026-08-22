using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

public class PlayerHallucinationController : NetworkBehaviour
{
    [Header("Fog Detection")]
    public float fogMeterIncreaseRate = 15f;
    public float fogMeterDecreaseRate = 8f;
    public float maxFogMeter = 100f;
    
    [Header("Hallucinations")]
    public float hallucinationThreshold = 50f; // When effects start happening
    public float minTimeBetweenHallucinations = 10f;
    public float maxTimeBetweenHallucinations = 25f;
    
    [Header("Visual Hallucinations")]
    public GameObject[] phantomPrefabs; // Fake monsters or shadows
    public float minSpawnDistance = 10f;
    public float maxSpawnDistance = 20f;

    [Header("Audio Hallucinations")]
    public AudioClip[] creepySounds; // Ambience or fake player noises
    [Range(0f, 1f)] public float soundVolume = 0.6f;

    private float currentFogMeter = 0f;
    private int fogVolumesOverlapping = 0;
    private float nextHallucinationTime;

    private bool IsLocalPlayer()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            return IsOwner;
        return true; // Allow testing without starting host
    }

    public override void OnNetworkSpawn()
    {
        if (!IsLocalPlayer())
        {
            enabled = false;
            return;
        }
        
        ScheduleNextHallucination();
    }

    private void Start()
    {
        if (!IsLocalPlayer())
            enabled = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsLocalPlayer()) return;
        
        if (other.GetComponentInParent<CursedFogVolume>() != null)
        {
            fogVolumesOverlapping++;
            Debug.Log($"[Hallucination] Entered fog! Overlapping: {fogVolumesOverlapping}");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsLocalPlayer()) return;

        if (other.GetComponentInParent<CursedFogVolume>() != null)
        {
            fogVolumesOverlapping--;
            if (fogVolumesOverlapping < 0) fogVolumesOverlapping = 0;
            Debug.Log($"[Hallucination] Exited fog! Overlapping: {fogVolumesOverlapping}");
        }
    }

    private void Update()
    {
        if (!IsLocalPlayer()) return;

        // Update the meter based on whether we are inside the fog
        if (fogVolumesOverlapping > 0)
        {
            currentFogMeter += fogMeterIncreaseRate * Time.deltaTime;
        }
        else
        {
            currentFogMeter -= fogMeterDecreaseRate * Time.deltaTime;
        }
        currentFogMeter = Mathf.Clamp(currentFogMeter, 0f, maxFogMeter);

        // Link 'currentFogMeter / maxFogMeter' to the PlayerVignetteEffect
        PlayerVignetteEffect vignette = GetComponent<PlayerVignetteEffect>();
        if (vignette != null)
        {
            float fogRatio = currentFogMeter / maxFogMeter;
            // Lerp intensity up to 0.5f so it gets quite dark but doesn't blind completely
            vignette.hallucinationIntensity = Mathf.Lerp(0f, 0.5f, fogRatio);
            
            // Optionally, we can also tint it slightly purple or darker by 
            // relying on the externalThreat logic, but hallucinationIntensity alone darkens it using the vignetteColor (which is black by default).
        }

        // Check if we reached the threshold to start hallucinating
        if (currentFogMeter >= hallucinationThreshold)
        {
            if (Time.time >= nextHallucinationTime)
            {
                TriggerHallucination();
                ScheduleNextHallucination();
            }
        }
    }

    private void ScheduleNextHallucination()
    {
        nextHallucinationTime = Time.time + Random.Range(minTimeBetweenHallucinations, maxTimeBetweenHallucinations);
    }

    private void TriggerHallucination()
    {
        // 50% chance for Audio, 50% chance for Visual (if prefabs are assigned)
        bool doVisual = (phantomPrefabs != null && phantomPrefabs.Length > 0);
        bool doAudio = (creepySounds != null && creepySounds.Length > 0);

        if (doVisual && doAudio)
        {
            if (Random.value > 0.5f) TriggerVisualHallucination();
            else TriggerAudioHallucination();
        }
        else if (doVisual)
        {
            TriggerVisualHallucination();
        }
        else if (doAudio)
        {
            TriggerAudioHallucination();
        }
    }

    private void TriggerAudioHallucination()
    {
        AudioClip clip = creepySounds[Random.Range(0, creepySounds.Length)];
        
        // Pick a random position around the player to play the sound
        Vector3 randomDir = Random.onUnitSphere;
        randomDir.y = 0; // Keep it on the same horizontal plane
        randomDir.Normalize();
        
        Vector3 soundPos = transform.position + randomDir * Random.Range(5f, 12f);
        AudioSource.PlayClipAtPoint(clip, soundPos, soundVolume);
    }

    private void TriggerVisualHallucination()
    {
        GameObject prefab = phantomPrefabs[Random.Range(0, phantomPrefabs.Length)];
        
        // Spawn slightly out of view (behind the player or to the sides)
        Vector3 spawnDir = -transform.forward; // Backwards
        
        // Add random angle between -60 and +60 degrees from the back
        float randomAngle = Random.Range(-60f, 60f);
        spawnDir = Quaternion.Euler(0, randomAngle, 0) * spawnDir;
        
        Vector3 spawnPos = transform.position + spawnDir * Random.Range(minSpawnDistance, maxSpawnDistance);
        
        // Snap to floor
        if (Physics.Raycast(spawnPos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f))
        {
            spawnPos = hit.point;
        }

        GameObject phantom = Instantiate(prefab, spawnPos, Quaternion.LookRotation(transform.position - spawnPos));
        Debug.Log($"[Hallucination] Spawning visual phantom: {prefab.name} at distance {Vector3.Distance(transform.position, spawnPos):F1}m");
        
        // Ensure the phantom has the script to move towards the player and disappear
        HallucinationPhantom phantomScript = phantom.GetComponent<HallucinationPhantom>();
        if (phantomScript == null) phantomScript = phantom.AddComponent<HallucinationPhantom>();
        
        phantomScript.target = transform; // Set this player as the target
    }
}
