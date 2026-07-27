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

    [Header("Reacao a Agua")]
    public float waterReactionCooldown = 0.25f;
    public float waterFleeDistance = 10f;
    public float waterFleeDuration = 4f;
    public float waterFleeSpeed = 7f;

    [Header("Contaminacao Opcional")]
    public GameObject contaminationTrailPrefab;
    public float trailSpawnInterval = 0.75f;

    private Transform player;
    private PlayerStatus playerStatus;
    private NavMeshAgent agent;
    private float proximoAtaqueEm;
    private float tempoSemSom;
    private float trailTimer;
    private float nextWaterReactionTime;
    private float waterFleeTimer;
    private Vector3 ultimoSomPosicao;
    private Vector3 waterFleeTarget;
    private bool ouviuSom;
    private bool perseguindo;
    private bool fugindoDaAgua;

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
        if (!EnemyAuthority.CanRunGameplay())
            return;

        ResolvePlayer();
        UpdateMemory();
        if (UpdateWaterFlee())
        {
            TryLeaveTrail();
            return;
        }

        UpdateMovement();
        TryAttack();
        TryLeaveTrail();
    }

    void ResolvePlayer()
    {
        if (EnemyTargeting.IsValidTarget(playerStatus) &&
            player == playerStatus.transform)
        {
            return;
        }

        if (EnemyTargeting.TryFindClosestPlayer(
            transform.position,
            out playerStatus,
            out player))
        {
            return;
        }

        player = null;
        playerStatus = null;
    }

    void OnNoiseHeard(Vector3 position, float radius, GameObject source)
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        float hearingRadius = radius * multiplicadorAudicao;
        if (Vector3.Distance(transform.position, position) > hearingRadius) return;

        ultimoSomPosicao = position;
        ouviuSom = true;
        tempoSemSom = 0f;
        perseguindo = EnemyTargeting.TryGetPlayerStatus(
            source,
            out PlayerStatus noisePlayer);

        if (perseguindo)
        {
            playerStatus = noisePlayer;
            player = noisePlayer.transform;
        }
    }

    void UpdateMemory()
    {
        if (fugindoDaAgua) return;
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

    bool UpdateWaterFlee()
    {
        if (!fugindoDaAgua)
            return false;

        waterFleeTimer -= Time.deltaTime;
        MoveTo(waterFleeTarget, waterFleeSpeed);

        bool reachedTarget = Vector3.Distance(transform.position, waterFleeTarget) <= distanciaParaInvestigar;
        if (waterFleeTimer <= 0f || reachedTarget)
        {
            fugindoDaAgua = false;
            ouviuSom = false;
            perseguindo = false;
            StopAgent();
        }

        return true;
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
        if (fugindoDaAgua) return;
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

    public void OnWaterHit()
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        ReactToWater(GetFallbackWaterSource());
    }

    public void ReceiveWaterHit()
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        ReactToWater(GetFallbackWaterSource());
    }

    public void ReceiveWaterHit(Vector3 sourcePosition)
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        ReactToWater(sourcePosition);
    }

    public void SprayedWithWater()
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        ReactToWater(GetFallbackWaterSource());
    }

    void OnParticleCollision(GameObject other)
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        if (other == null) return;

        WaterParticleCollisionRelay water = other.GetComponent<WaterParticleCollisionRelay>();
        if (water == null)
            water = other.GetComponentInParent<WaterParticleCollisionRelay>();

        if (water != null)
            ReactToWater(water.transform.position);
    }

    Vector3 GetFallbackWaterSource()
    {
        return player != null ? player.position : transform.position - transform.forward;
    }

    void ReactToWater(Vector3 sourcePosition)
    {
        if (Time.time < nextWaterReactionTime)
            return;

        nextWaterReactionTime = Time.time + waterReactionCooldown;
        BeginWaterFlee(sourcePosition);
    }

    void BeginWaterFlee(Vector3 sourcePosition)
    {
        Vector3 awayFromWater = transform.position - sourcePosition;
        awayFromWater.y = 0f;
        if (awayFromWater.sqrMagnitude <= 0.001f)
            awayFromWater = -transform.forward;

        Vector3 wantedTarget = transform.position + awayFromWater.normalized * waterFleeDistance;
        waterFleeTarget = wantedTarget;

        if (NavMesh.SamplePosition(wantedTarget, out NavMeshHit hit, waterFleeDistance, NavMesh.AllAreas))
            waterFleeTarget = hit.position;

        waterFleeTimer = waterFleeDuration;
        fugindoDaAgua = true;
        ouviuSom = false;
        perseguindo = false;
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
