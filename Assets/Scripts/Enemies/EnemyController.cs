using System.Collections.Generic;
using Core;
using Events;
using FSM;
using Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace Enemies
{
    /// <summary>
    /// FSM CONTEXT for regular enemies (both zombie types). It owns the shared tools the states use
    /// (agent, animator, target, senses) and the <see cref="StateMachine"/>, but holds no behaviour
    /// rules itself: those live in the four state classes.
    ///
    ///   Idle  --notices player-->  Chase  --in reach-->  Attack
    ///   Chase --loses player-->    Idle   Attack --out of reach--> Chase
    ///   any state --Health.Died--> Dead
    ///
    /// The enemy types differ only in data: an EnemyStats asset, a Health value and a model/Animator.
    ///
    /// NAVMESH TIMING: the level is generated and baked at runtime, so an enemy placed before the bake has no
    /// NavMesh under it. The NavMeshAgent stays disabled until a NavMesh is found near the enemy, and the
    /// state machine starts only then. Untick the NavMeshAgent on the prefab, otherwise Unity logs an error
    /// before this script can hold it back.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public class EnemyController : MonoBehaviour
    {
        // Height above the player's feet that sight checks and attacks aim at (the player is 4 tall).
        private const float TargetChestHeight = 2f;

        // The hit still lands if the player stepped back a little during the swing.
        private const float ReachTolerance = 1.25f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int DeadHash = Animator.StringToHash("Dead");

        [Header("Data")]
        [SerializeField] private EnemyStats stats;

        [Header("References")]
        [SerializeField] private Animator animator;
        [Tooltip("Optional. Where sight checks start. Defaults to 3 units above the feet.")]
        [SerializeField] private Transform eye;

        [Header("Senses")]
        [Tooltip("Layers that block line of sight: the level geometry. Do NOT include the Player or Enemy layers.")]
        [SerializeField] private LayerMask sightBlockers = 1;

        /// <summary>Every active enemy. Disorient uses it to find someone for an enemy to fight.</summary>
        public static readonly List<EnemyController> All = new();

        private Transform _playerTarget;
        private Health _playerHealth;
        private IDamageable _playerDamageable;
        private bool _wasStopped;

        private Health _health;
        private Health _targetHealth;
        private IDamageable _targetDamageable;
        private bool _ready;

        public EnemyStats Stats => stats;
        public NavMeshAgent Agent { get; private set; }
        public Transform Target { get; private set; }
        public StateMachine Fsm { get; } = new();

        // States are created once and reused, so switching state never allocates.
        public EnemyIdleState Idle { get; private set; }
        public EnemyChaseState Chase { get; private set; }
        public EnemyAttackState Attack { get; private set; }
        public EnemyDeadState Dead { get; private set; }

        public bool HasTarget => Target && !_targetHealth.IsDead;

        /// <summary>True while the enemy is fighting another enemy instead of the player (Disorient).</summary>
        public bool TargetIsEnemy { get; private set; }

        /// <summary>Frozen in place by the camera flash. The state machine does not tick and the animation stops.</summary>
        public bool Stunned { get; private set; }

        public bool IsDead => _health.IsDead;

        /// <summary>Flat (XZ) distance to the target. Only valid when HasTarget.</summary>
        public float DistanceToTarget
        {
            get
            {
                var offset = Target.position - transform.position;
                offset.y = 0f;
                return offset.magnitude;
            }
        }

        private Vector3 EyePosition => eye ? eye.position : transform.position + Vector3.up * 3f;

        private void Awake()
        {
            Agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();

            if (!animator) animator = GetComponentInChildren<Animator>();

            // Held back until a NavMesh exists under us, see the class comment.
            Agent.enabled = false;

            Idle = new EnemyIdleState(this);
            Chase = new EnemyChaseState(this);
            Attack = new EnemyAttackState(this);
            Dead = new EnemyDeadState(this);

            var player = FindAnyObjectByType<PlayerController>();
            if (player)
            {
                Target = player.transform;
                _targetHealth = player.GetComponent<Health>();
                _targetDamageable = player.GetComponent<IDamageable>();

                _playerTarget = Target;
                _playerHealth = _targetHealth;
                _playerDamageable = _targetDamageable;
            }
            else
            {
                Debug.LogWarning("[EnemyController] No PlayerController in the scene, this enemy will stay idle.", this);
            }
        }

        private void OnEnable()
        {
            All.Add(this);
            _health.Died += OnDied;

            if (GameServices.TryGet(out EventBus bus))
            {
                bus.Subscribe<HealthChangedEvent>(OnHealthChanged);
            }
        }

        private void OnDisable()
        {
            All.Remove(this);
            _health.Died -= OnDied;

            if (GameServices.TryGet(out EventBus bus))
            {
                bus.Unsubscribe<HealthChangedEvent>(OnHealthChanged);
            }
        }

        private void Update()
        {
            if (!_ready)
            {
                TryActivateOnNavMesh();
                return;
            }

            if (Stunned)
            {
                HoldStill();
                return;
            }

            Fsm.Tick(Time.deltaTime);
            UpdateLocomotionAnimation();
        }

        private void TryActivateOnNavMesh()
        {
            if (_health.IsDead || !NavMesh.SamplePosition(transform.position, out var hit, 4f, NavMesh.AllAreas))
            {
                return;
            }

            transform.position = hit.position;
            Agent.enabled = true;
            Agent.speed = stats.moveSpeed;
            Agent.stoppingDistance = 0f;

            _ready = true;
            Fsm.ChangeState(Idle);
        }

        // The Animator's Speed parameter is the real agent speed divided by the enemy's fastest speed.
        // For an enemy that never sprints, 1 is Walk. For one that does, Walk sits at MoveSpeed / MaxSpeed
        // and 1 is Run. It follows the real agent speed, so slowing down looks right.
        private void UpdateLocomotionAnimation()
        {
            if (!animator || !Agent.enabled) return;

            var normalized = Mathf.Clamp01(Agent.velocity.magnitude / stats.MaxSpeed);
            animator.SetFloat(SpeedHash, normalized, 0.1f, Time.deltaTime);
        }

        // Camera flash effects (see EnemyStatus)

        /// <summary>Freezes or releases the enemy. While frozen nothing ticks, so an attack in progress waits too.</summary>
        public void SetStunned(bool stunned)
        {
            if (Stunned == stunned) return;
            Stunned = stunned;

            if (stunned)
            {
                if (Agent.enabled && Agent.isOnNavMesh)
                {
                    _wasStopped = Agent.isStopped;
                    Agent.isStopped = true;
                    Agent.velocity = Vector3.zero;
                }

                if (animator) animator.speed = 0f;
            }
            else
            {
                if (Agent.enabled && Agent.isOnNavMesh) Agent.isStopped = _wasStopped;
                if (animator) animator.speed = 1f;
            }
        }

        private void HoldStill()
        {
            if (Agent.enabled && Agent.isOnNavMesh) Agent.velocity = Vector3.zero;
        }

        /// <summary>Disorient: fight this enemy instead of the player. The target must have a Health.</summary>
        public void SetTargetOverride(Transform enemy)
        {
            if (!enemy || !enemy.TryGetComponent<Health>(out var health) || !enemy.TryGetComponent<IDamageable>(out var damageable)) return;

            Target = enemy;
            _targetHealth = health;
            _targetDamageable = damageable;
            TargetIsEnemy = true;

            if (_ready && !_health.IsDead && !Stunned) BeginChase();
        }

        /// <summary>Back to hunting the player. The state machine starts over from Idle and notices the player again.</summary>
        public void ResetTarget()
        {
            if (!TargetIsEnemy) return;

            Target = _playerTarget;
            _targetHealth = _playerHealth;
            _targetDamageable = _playerDamageable;
            TargetIsEnemy = false;

            if (_ready && !_health.IsDead && !Stunned) Fsm.ChangeState(Idle);
        }

        // Used by the states

        public bool CanSeeTarget()
        {
            if (!HasTarget) return false;

            var aimPoint = Target.position + Vector3.up * TargetChestHeight;
            return !Physics.Linecast(EyePosition, aimPoint, sightBlockers, QueryTriggerInteraction.Ignore);
        }

        public bool CanNoticeTarget()
        {
            return HasTarget && DistanceToTarget <= stats.detectionRange && CanSeeTarget();
        }

        public void FaceTarget(float deltaTime)
        {
            var flat = Target.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(flat), stats.turnSpeed * deltaTime);
        }

        public void PlayAttackAnimation()
        {
            if (animator) animator.SetTrigger(AttackHash);
        }

        public void PlayDeathAnimation()
        {
            if (animator) animator.SetTrigger(DeadHash);
        }

        /// <summary>Lands the hit if the target is still within reach (with a little tolerance).</summary>
        public void DealDamage()
        {
            if (!HasTarget || DistanceToTarget > stats.attackRange * ReachTolerance) return;

            _targetDamageable.TakeDamage(stats.attackDamage, gameObject);
        }

        /// <summary>
        /// Starts a chase because the enemy just noticed the player (or was shot). Enemies that have an
        /// opening sprint use it here. Going back to Chase after an attack does NOT sprint, so it
        /// calls Fsm.ChangeState directly instead.
        /// </summary>
        public void BeginChase()
        {
            Chase.SprintRequested = stats.CanSprint;
            Fsm.ChangeState(Chase);
        }

        public bool IsTargetInAttackReach()
        {
            return HasTarget && DistanceToTarget <= stats.attackRange * ReachTolerance;
        }

        // Events

        private void OnDied()
        {
            SetStunned(false); // the death animation must be able to play
            if (GameServices.TryGet(out EventBus bus))
            {
                bus.Publish(new EnemyKilledEvent(stats.enemyId, transform.position));
            }

            Fsm.ChangeState(Dead);
        }

        // Being shot wakes an idle enemy up, even if it never saw the player.
        private void OnHealthChanged(HealthChangedEvent e)
        {
            if (e.Entity != gameObject || e.Delta >= 0f) return;

            if (_ready && Fsm.Current == Idle)
            {
                BeginChase();
            }
        }
    }
}