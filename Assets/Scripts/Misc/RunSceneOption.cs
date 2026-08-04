using System;
using UnityEngine;

[Serializable]
public class RunSceneOption
{
    public string regionName = "Hospital";
    public string sceneName = "Game";
    [Min(0f)] public float weight = 1f;
}
