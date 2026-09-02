using UnityEngine;

public class PlayableAdInteractable : MonoBehaviour
{
    public bool isCleaned = false;
    public ParticleSystem cleanEffect;
    public AudioClip cleanSound;

    public void Interact()
    {
        if (isCleaned) return;

        isCleaned = true;
        
        if (cleanEffect != null)
        {
            cleanEffect.transform.SetParent(null);
            cleanEffect.Play();
            Destroy(cleanEffect.gameObject, 2f);
        }

        if (cleanSound != null)
        {
            AudioSource.PlayClipAtPoint(cleanSound, transform.position);
        }

        gameObject.SetActive(false);
    }
}
