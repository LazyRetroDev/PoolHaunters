using UnityEngine;

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
            Instantiate(dirtPrefab, transform.position, transform.rotation);
        else if (GetComponent<DirtSpot>() == null)
            gameObject.AddComponent<DirtSpot>();

        if (linkedPhoto != null)
            linkedPhoto.InvalidateFromLinkedDecal();

        if (destroyDecalOnContamination && dirtPrefab != null)
            Destroy(gameObject);
    }
}
