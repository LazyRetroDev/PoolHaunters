using UnityEngine;
using Unity.Netcode;

public class PhotographerDecal : MonoBehaviour
{
    public float contaminationDelay = 20f;
    public GameObject dirtPrefab;
    public bool destroyDecalOnContamination = true;

    private float timer;
    private bool resolved;
    private PhotoItem linkedPhoto;

    void OnEnable()
    {
        timer = contaminationDelay;
    }

    void Update()
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        if (resolved || contaminationDelay <= 0f) return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
            ContaminateArea();
    }

    public void LinkPhoto(PhotoItem photoItem)
    {
        linkedPhoto = photoItem;
    }

    public void ClearFromPhoto()
    {
        if (resolved) return;

        resolved = true;
        if (destroyDecalOnContamination)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }

    void ContaminateArea()
    {
        if (resolved) return;
        resolved = true;

        if (dirtPrefab != null)
            SpawnDirtPrefab();
        else
            Debug.LogWarning("PhotographerDecal needs a dirtPrefab assigned to create contamination.");

        if (linkedPhoto != null)
            linkedPhoto.InvalidateFromLinkedDecal();

        if (destroyDecalOnContamination && dirtPrefab != null)
            Destroy(gameObject);
    }

    void SpawnDirtPrefab()
    {
        GameObject dirtObject = Instantiate(
            dirtPrefab,
            transform.position,
            transform.rotation);

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return;

        if (!networkManager.IsServer)
        {
            Destroy(dirtObject);
            return;
        }

        NetworkObject networkObject = dirtObject.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogWarning(
                $"Dirt prefab '{dirtPrefab.name}' needs a NetworkObject for multiplayer spawning.");
            Destroy(dirtObject);
            return;
        }

        if (!networkObject.IsSpawned)
            networkObject.Spawn(true);
    }

}
