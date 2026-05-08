using UnityEngine;

public class TubaraoBehavior : MonoBehaviour
{
    [Header("Configurações de Movimento")]
    public float velocidade = 3f;

    public float velocidadeRotacao = 5f;

    public float distanciaDeDeteccao = 10f;

    private Transform player;

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player").transform;
    }

    void Update()
    {
        if (player == null) return;

        float distanciaAtual = Vector3.Distance(transform.position, player.position);

        if (distanciaAtual <= distanciaDeDeteccao)
        {
            Vector3 direcaoProPlayer = player.position - transform.position;

            direcaoProPlayer.y = 0;

            Quaternion rotacaoAlvo = Quaternion.LookRotation(direcaoProPlayer);

            transform.rotation = Quaternion.Slerp(transform.rotation, rotacaoAlvo, velocidadeRotacao * Time.deltaTime);

            transform.position = Vector3.MoveTowards(transform.position, player.position, velocidade * Time.deltaTime);
        }
    }
}
