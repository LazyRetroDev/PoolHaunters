using UnityEngine;

public class TubaraoBehavior : MonoBehaviour
{
    [Header("Configuracoes de Movimento")]
    public float velocidade = 3f;
    public float velocidadeRotacao = 5f;
    public float distanciaDeDeteccao = 10f;

    [Header("Ataque")]
    public float distanciaDeAtaque = 1.5f;
    public float danoPorAtaque = 10f;
    public float intervaloEntreAtaques = 0.5f;

    private Transform player;
    private PlayerStatus playerStatus;
    private float proximoAtaqueEm;

    void Start()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject == null) return;

        player = playerObject.transform;
        playerStatus = playerObject.GetComponent<PlayerStatus>();
    }

    void Update()
    {
        if (player == null || playerStatus == null) return;

        float distanciaAtual = Vector3.Distance(transform.position, player.position);
        if (distanciaAtual > distanciaDeDeteccao) return;

        Vector3 direcaoProPlayer = player.position - transform.position;
        direcaoProPlayer.y = 0f;

        if (direcaoProPlayer != Vector3.zero)
        {
            Quaternion rotacaoAlvo = Quaternion.LookRotation(direcaoProPlayer);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotacaoAlvo, velocidadeRotacao * Time.deltaTime);
        }

        transform.position = Vector3.MoveTowards(transform.position, player.position, velocidade * Time.deltaTime);

        if (distanciaAtual <= distanciaDeAtaque && Time.time >= proximoAtaqueEm)
        {
            playerStatus.TakeDamage(danoPorAtaque);
            proximoAtaqueEm = Time.time + intervaloEntreAtaques;
        }
    }
}
