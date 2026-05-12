using UnityEngine;
using System.Collections.Generic;

public class TimeCamperManager : MonoBehaviour
{
    public static TimeCamperManager Instance;
    public int maxEntities = 3;
    private List<TimeCamper> activeEntities = new List<TimeCamper>();

    void Awake()
    {
        Instance = this;
    }

    public bool CanSpawn()
    {
        activeEntities.RemoveAll(e => e == null);
        return activeEntities.Count < maxEntities;
    }

    public void Register(TimeCamper entity)
    {
        if (!activeEntities.Contains(entity))
            activeEntities.Add(entity);
    }

    public void Unregister(TimeCamper entity)
    {
        activeEntities.Remove(entity);
    }
}