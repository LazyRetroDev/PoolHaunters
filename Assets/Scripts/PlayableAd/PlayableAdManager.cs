using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayableAdManager : MonoBehaviour
{
    [Header("Ad Settings")]
    public float timeLimit = 15f;
    public int totalDirtToClean = 3;
    
    [Header("References")]
    public PlayableAdUI uiManager;
    public Camera adCamera;
    
    [Header("Jumpscare Settings")]
    public GameObject hallucinationGhost;
    public AudioClip jumpscareSound;
    public AudioSource jumpscareSource;

    [Header("Pool Visuals")]
    public GameObject dirtyWaterVisual;
    public GameObject cleanWaterVisual;

    private float timer;
    private int dirtCleaned = 0;
    private bool gameEnded = false;

    void Start()
    {
        timer = timeLimit;
        if (hallucinationGhost != null)
            hallucinationGhost.SetActive(false);

        if (adCamera == null)
            adCamera = Camera.main;

        if (dirtyWaterVisual != null) dirtyWaterVisual.SetActive(true);
        if (cleanWaterVisual != null) cleanWaterVisual.SetActive(false);
    }

    void Update()
    {
        if (gameEnded) return;

        UpdateTimer();
        HandleInput();
    }

    void UpdateTimer()
    {
        timer -= Time.deltaTime;
        
        if (uiManager != null)
            uiManager.UpdateTimer(timer);

        if (timer <= 0)
        {
            EndGame(false);
        }
    }

    void HandleInput()
    {
        // Usa o New Input System (Pointer cuida de Mouse e Touch simultaneamente)
        if (Pointer.current != null && Pointer.current.press.isPressed)
        {
            Vector2 screenPos = Pointer.current.position.ReadValue();
            Ray ray = adCamera.ScreenPointToRay(screenPos);
            
            // Usamos RaycastAll caso haja algum colisor invisível de água bloqueando a sujeira
            RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);
            Debug.Log($"[PlayableAd] Clicked! Raycast hit {hits.Length} objects.");
            
            foreach (RaycastHit hit in hits)
            {
                Debug.Log($"[PlayableAd] Hit: {hit.collider.gameObject.name} (Layer: {hit.collider.gameObject.layer})");
                PlayableAdInteractable interactable = hit.collider.GetComponentInParent<PlayableAdInteractable>();
                
                if (interactable != null)
                {
                    if (!interactable.isCleaned)
                    {
                        Debug.Log($"[PlayableAd] Found uncleaned interactable on: {interactable.gameObject.name}");
                        interactable.Interact();
                        dirtCleaned++;
                        
                        if (dirtCleaned >= totalDirtToClean)
                        {
                            EndGame(true);
                        }
                        
                        // Sai do loop, pois já limpamos uma sujeira com este clique
                        break;
                    }
                    else
                    {
                        Debug.Log($"[PlayableAd] Found interactable, but it was already cleaned: {interactable.gameObject.name}");
                    }
                }
            }
        }
    }

    void EndGame(bool isVictory)
    {
        if (gameEnded) return;
        gameEnded = true;

        if (isVictory)
        {
            if (dirtyWaterVisual != null) dirtyWaterVisual.SetActive(false);
            if (cleanWaterVisual != null) cleanWaterVisual.SetActive(true);
        }

        StartCoroutine(JumpscareRoutine(isVictory));
    }

    IEnumerator JumpscareRoutine(bool isVictory)
    {
        // 1. Mostrar o fantasma (susto) independentemente de ganhar ou perder para chocar o jogador
        if (hallucinationGhost != null)
        {
            hallucinationGhost.SetActive(true);
        }

        if (jumpscareSource != null && jumpscareSound != null)
        {
            jumpscareSource.PlayOneShot(jumpscareSound);
        }

        // Deixar a tela do susto por 1 segundo
        yield return new WaitForSeconds(1f);

        // 2. Mostrar o Call To Action para forçar o download
        if (uiManager != null)
        {
            uiManager.ShowCTA(isVictory);
        }
    }
}
