using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class Enemy : MonoBehaviour, IDamageable
{
    [Header("Target")]
    public Transform player;

    [Header("Weaving movement")]
    public float weaveAmplitude = 4f;
    public float weaveFrequency = 1f;

    [Header("Combat")]
    public float attackRange = 1.5f;
    public float attackCooldown = 1.5f;
    public float attackDamage = 0.2f;
    public float maxHealth = 2f;

    [Header("Pathing")]
    public float repathInterval = 0.2f;

    private NavMeshAgent agent;
    private Animator animator;
    private float phase;
    private float repathTimer;
    private float lastAttackTime;
    private float health;
    private bool isDead;

    private GameService gameService;

    [Header("Hit reaction")]
    [Tooltip("How long the spider is stunned after taking a hit (seconds)")]
    public float stunDuration = 0.8f;

    private float stunTimer;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        phase = Random.Range(0f, Mathf.PI * 2f);
        health = maxHealth;

        agent.avoidancePriority = Random.Range(30, 70);
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance;
    }

    private void Start()
    {
        gameService = FindAnyObjectByType<GameService>();
    }

    void Update()
    {
        if (isDead || player == null) return;

        if (stunTimer > 0f)
        {
            stunTimer -= Time.deltaTime;
            agent.isStopped = true;
            if (animator != null)
                animator.SetFloat("Speed", 0f);
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);


        if (distance <= attackRange)
            Attack();
        else
            Chase();

        if (animator != null)
            animator.SetFloat("Speed", agent.velocity.magnitude);
    }

    void Chase()
    {
        agent.isStopped = false;

        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f)
        {
            repathTimer = repathInterval;
            SetWeavingDestination();
        }
    }


    void Attack()
    {
        agent.isStopped = true;
        FacePlayer();

        if (Time.time >= lastAttackTime + attackCooldown)
        {  
            gameService.PlayerHit(attackDamage);
            lastAttackTime = Time.time;
            animator.SetTrigger("Attack");
        }
    }

    void FacePlayer()
    {
        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 8f);
        }
    }

    void SetWeavingDestination()
    {
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        Vector3 dir = toPlayer.normalized;
        Vector3 side = Vector3.Cross(Vector3.up, dir);

        float weave = Mathf.Sin(Time.time * weaveFrequency + phase) * weaveAmplitude;
        weave *= Mathf.Clamp01(distance / 1.5f);

        Vector3 target = player.position + side * weave;

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
        else
            agent.SetDestination(player.position);
    }

    // from IDamageable — called by the player's bullet/weapon
    public void TakeDamage(float amount)
    {
        if (isDead) return;

        health -= amount;

        if (health <= 0)
            Die();
        else
            animator.SetTrigger("Hit");
        SetStun();
    }


    void Die()
    {
        agent.speed = 0;
        isDead = true;
        agent.isStopped = true;
        animator.SetBool("Dead", true);
        Destroy(gameObject, 3f);
    }

    void SetStun()
    {
        stunTimer = stunDuration;
    }
}