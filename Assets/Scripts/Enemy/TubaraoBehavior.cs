using UnityEngine;
using UnityEngine.AI;

public class TubaraoBehavior : MonoBehaviour
{
    [Header("Movimento")]
    public float velocidadeInvestigacao = 3f;
    public float velocidadePerseguicao = 6.5f;
    public float velocidadeRotacao = 6f;
    public float distanciaParaInvestigar = 1.25f;

    [Header("Audio / Som")]
    public float multiplicadorAudicao = 1f;
    public float tempoLembrandoSom = 6f;

    [Header("Ataque")]
    public float distanciaDeAtaque = 1.5f;
    public float danoPorAtaque = 10f;
    public float intervaloEntreAtaques = 0.5f;

    [Header("Contaminacao Opcional")]
    public GameObject contaminationTrailPrefab;
    public float trailSpawnInterval = 0.75f;

    private Transform player;
    private PlayerStatus playerStatus;
    private NavMeshAgent agent;
    private float proximoAtaqueEm;
    private float tempoSemSom;
    private float trailTimer;
    private Vector3 ultimoSomPosicao;
    private bool ouviuSom;
    private bool perseguindo;

    void OnEnable()
    {
        NoiseEvent.OnNoiseEmitted += OnNoiseHeard;
    }

    void OnDisable()
    {
        NoiseEvent.OnNoiseEmitted -= OnNoiseHeard;
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        ResolvePlayer();

        if (agent != null)
        {
            agent.speed = velocidadeInvestigacao;
            agent.angularSpeed = 360f;
        }
    }

    void Update()
    {
        ResolvePlayer();
        UpdateMemory();
        UpdateMovement();
        TryAttack();
        TryLeaveTrail();
    }

    void ResolvePlayer()
    {
        if (player != null && playerStatus != null) return;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject == null) return;

        player = playerObject.transform;
        playerStatus = playerObject.GetComponent<PlayerStatus>();
    }

    void OnNoiseHeard(Vector3 position, float radius, GameObject source)
    {
        float hearingRadius = radius * multiplicadorAudicao;
        if (Vector3.Distance(transform.position, position) > hearingRadius) return;

        ultimoSomPosicao = position;
        ouviuSom = true;
        tempoSemSom = 0f;
        perseguindo = player != null && source != null && source.CompareTag("Player");
    }

    void UpdateMemory()
    {
        if (!ouviuSom) return;

        tempoSemSom += Time.deltaTime;
        if (tempoSemSom <= tempoLembrandoSom) return;

        ouviuSom = false;
        perseguindo = false;
        StopAgent();
    }

    void UpdateMovement()
    {
        if (!ouviuSom)
        {
            StopAgent();
            return;
        }

        Vector3 destino = ultimoSomPosicao;
        MoveTo(destino, perseguindo ? velocidadePerseguicao : velocidadeInvestigacao);

        if (Vector3.Distance(transform.position, ultimoSomPosicao) <= distanciaParaInvestigar)
            StopAgent();
    }

    void MoveTo(Vector3 target, float speed)
    {
        FaceTarget(target);

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.speed = speed;
            agent.isStopped = false;
            agent.SetDestination(target);
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
    }

    void StopAgent()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.isStopped = true;
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 direcao = target - transform.position;
        direcao.y = 0f;
        if (direcao.sqrMagnitude <= 0.001f) return;

        Quaternion rotacaoAlvo = Quaternion.LookRotation(direcao.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, rotacaoAlvo, velocidadeRotacao * Time.deltaTime);
    }

    void TryAttack()
    {
        if (player == null || playerStatus == null || Time.time < proximoAtaqueEm) return;
        if (!perseguindo) return;
        if (Vector3.Distance(transform.position, player.position) > distanciaDeAtaque) return;

        playerStatus.TakeDamage(danoPorAtaque);
        proximoAtaqueEm = Time.time + intervaloEntreAtaques;
        tempoSemSom = 0f;
    }

    void TryLeaveTrail()
    {
        if (contaminationTrailPrefab == null || (!ouviuSom && !perseguindo)) return;

        trailTimer -= Time.deltaTime;
        if (trailTimer > 0f) return;

        trailTimer = trailSpawnInterval;
        Instantiate(contaminationTrailPrefab, transform.position, Quaternion.identity);
    }

    void OnDrawGizmosSelected()
    {
        if (ouviuSom)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(ultimoSomPosicao, distanciaParaInvestigar);
        }
    }
}
