using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PlayerRunState
{
    public float Water;
    public int WaterQuality;
    public List<string> ItemPrefabNames = new List<string>();
}

public static class PlayerRunStateTracker
{
    private static Dictionary<ulong, PlayerRunState> savedStates = new Dictionary<ulong, PlayerRunState>();

    public static void SaveAllPlayersState()
    {
        savedStates.Clear();
        PlayerStatus[] allPlayers = Object.FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        
        foreach (var playerStatus in allPlayers)
        {
            var state = new PlayerRunState();
            state.Water = playerStatus.GetCurrentWater();
            state.WaterQuality = (int)playerStatus.GetWaterQuality();
            
            PlayerInventory inventory = playerStatus.GetComponent<PlayerInventory>();
            if (inventory != null)
            {
                Item[] slots = inventory.GetSlots();
                if (slots != null)
                {
                    foreach (Item item in slots)
                    {
                        if (item != null)
                        {
                            string prefabName = GetPrefabNameForInstance(item.gameObject);
                            if (!string.IsNullOrEmpty(prefabName))
                            {
                                state.ItemPrefabNames.Add(prefabName);
                            }
                        }
                    }
                }
            }
            
            NetworkObject netObject = playerStatus.GetComponent<NetworkObject>();
            ulong clientId = netObject != null ? netObject.OwnerClientId : 0;

            savedStates[clientId] = state;
            Debug.Log($"[PlayerRunStateTracker] Saved state for client {clientId} with {state.ItemPrefabNames.Count} items.");
        }
    }

    public static PlayerRunState GetSavedState(ulong clientId)
    {
        if (savedStates.TryGetValue(clientId, out PlayerRunState state))
        {
            return state;
        }
        return null;
    }

    public static void Clear()
    {
        savedStates.Clear();
    }

    public static string GetPrefabNameForInstance(GameObject instance)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.NetworkConfig == null || instance == null)
            return null;

        string bestMatch = null;
        int longestMatch = 0;
        foreach (var networkPrefab in networkManager.NetworkConfig.Prefabs.Prefabs)
        {
            if (networkPrefab != null && networkPrefab.Prefab != null)
            {
                string pName = networkPrefab.Prefab.name;
                if (instance.name.StartsWith(pName) && pName.Length > longestMatch)
                {
                    bestMatch = pName;
                    longestMatch = pName.Length;
                }
            }
        }
        return bestMatch;
    }

    public static GameObject GetNetworkPrefabByName(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
            return null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.NetworkConfig == null)
            return null;

        foreach (var networkPrefab in networkManager.NetworkConfig.Prefabs.Prefabs)
        {
            if (networkPrefab != null && networkPrefab.Prefab != null)
            {
                if (networkPrefab.Prefab.name == prefabName)
                {
                    return networkPrefab.Prefab;
                }
            }
        }
        return null;
    }
}
