using UnityEngine;

public class WaterSourceDryable : MonoBehaviour
{
    [Header("State")]
    public bool startsDry = false;
    public bool isDry;

    [Header("Visuals")]
    public GameObject wetVisualRoot;
    public GameObject dryVisualRoot;
    public ParticleSystem[] waterParticles;
    public Renderer[] renderersToDisableWhenDry;
    public Collider[] collidersToDisableWhenDry;

    [Header("Optional")]
    public bool disableObjectWhenDry = false;
    public GameObject[] extraObjectsToDisableWhenDry;

    void Start()
    {
        isDry = startsDry;
        UpdateDryState();
    }

    public void DryOut()
    {
        if (isDry) return;

        isDry = true;
        UpdateDryState();
    }

    public void RestoreWater()
    {
        if (!isDry) return;

        isDry = false;
        UpdateDryState();
    }

    void UpdateDryState()
    {
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
