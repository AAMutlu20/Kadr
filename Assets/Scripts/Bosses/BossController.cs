using System;
using System.Collections;
using System.Collections.Generic;
using Core;
using Enemies;
using Events;
using Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace Bosses
{
    public enum BossForm
    {
        Normal,
        Quadruped
    }

    public enum BossAttackKind
    {
        /// <summary>A hit in front of the boss. Damage only.</summary>
        Melee,
        /// <summary>Same as Melee, named so the attack list reads clearly.</summary>
        Kick,
        /// <summary>Damage and a push back (Knockback is the starting speed in units per second).</summary>
        StrongKick,
        /// <summary>Charges up, then flashes the player if they are looking at the boss's head.</summary>
        Stun,
        /// <summary>Ground stomp: damage in a circle around the boss (Radius), with falloff towards the edge.</summary>
        Stomp
    }

    /// <summary>
    /// The final boss (Tree Head). He cannot be killed: the Health component is topped up every frame, so shots only
    /// show their hit effects. The fight ends when your own scripts call BossArena.End (the elevator, a generator...).
    ///
    /// Two forms:
    ///   Normal      walks upright. Swings, kicks, a strong kick that pushes the player away, the stun attack and the
    ///               ground stomp.
    ///   Quadruped   runs on four legs. Faster, attacks faster, only swipes.
    /// He changes form on a timer (with the To Quadruped / To Hooman animations), or goes quadruped early when the
    /// player keeps their distance.
    ///
    /// The stun attack: the boss stands still and charges (a light on his head ramps up). When the charge ends, a
    /// player who is LOOKING AT HIS HEAD (and can see it) is flashbanged: the screen goes white, then they are very
    /// slow for a few seconds. Looking away dodges it.
    ///
    /// Movement is the NavMeshAgent, animations are played by name from the Animator (build the controller with
    /// Tools > Kadr > Build Boss Animator From Selected FBX). Hit moments are fractions of each clip's length, so
    /// no frame rate matters and no Animation Events are needed.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public class BossController : MonoBehaviour
    {
        [Serializable]
        public class Attack
        {
            public string name = "Swing";
            [Tooltip("Name of the Animator state (the clip name after the |).")]
            public string state = "Swing";
            public BossAttackKind kind = BossAttackKind.Melee;
            public BossForm form = BossForm.Normal;
            [Tooltip("Damage to the player. Not used by the stun attack.")]
            public float damage = 15f;
            [Tooltip("Does not start when the player is closer than this (flat distance between centres).")]
            public float minRange;
            [Tooltip("Does not start when the player is farther than this. Keep it at or above Approach Distance, or he never starts it.")]
            public float maxRange = 5f;
            [Tooltip("How far from him the hit still lands, checked when it lands (after the lunge). 0 = Max Range. " +
                     "Bigger than Max Range, so a player who steps back is still hit.")]
            public float reach;
            [Tooltip("He steps this many units forward during the wind-up, so walking away does not dodge it. 0 = none.")]
            public float lunge;
            [Tooltip("Where in the animation the hit lands: 0 = first frame, 1 = last frame.")]
            [Range(0.05f, 0.95f)] public float hitAt = 0.5f;
            [Tooltip("Seconds before this attack can be used again.")]
            public float cooldown = 2.5f;
            [Tooltip("How often it is picked compared to the others that are possible.")]
            [Min(0.01f)] public float weight = 1f;
            [Tooltip("Strong kick: push speed. Stomp: radius of the shockwave.")]
            public float knockbackOrRadius;
            [Tooltip("Seconds he stays in the animation after the hit before he moves again. -1 = until the clip ends " +
                     "(0.25 for the stun, so he walks at the stunned player straight away).")]
            public float recovery = -1f;

            [NonSerialized] public float NextTime;
        }

        [Header("References (empty = found automatically)")]
        [SerializeField] private Animator animator;
        [SerializeField] private Transform player;
        [Tooltip("The head. The stun attack checks whether the player looks here. Empty = a point above the root (Head Height).")]
        [SerializeField] private Transform head;
        [Tooltip("Used when no head is assigned.")]
        [SerializeField] private float headHeight = 8f;
        [Tooltip("Optional light on the head. It glows brighter while the stun attack charges, as the warning.")]
        [SerializeField] private Light chargeLight;
        [SerializeField] private float chargeLightIntensity = 40f;

        [Header("Fire (incendiary rounds)")]
        [Tooltip("Size of the shared fire effect (the camera's Burn Effect Prefab) on the boss. He is much bigger than a zombie.")]
        [SerializeField] private float fireScale = 2.5f;
        [Tooltip("Range of the orange light that a burning boss casts.")]
        [SerializeField] private float fireLightRange = 16f;

        [Header("Attack visuals")]
        [Tooltip("Shows where an attack will land: an outline of its reach on the floor with a fill that grows until the hit.")]
        [SerializeField] private bool showTelegraphs = true;
        [Tooltip("Shows LOOK AWAY! on screen while the stun charges and the player is looking at his head.")]
        [SerializeField] private bool lookAwayWarning = true;
        [Tooltip("Size of the built-in stun effects (glow, sparks, flash). Raise it for a bigger head.")]
        [SerializeField] private float effectScale = 1f;
        [Tooltip("How far above the floor the telegraph is drawn, so it does not flicker with the ground.")]
        [SerializeField] private float telegraphHeight = 0.12f;
        [Tooltip("Optional. Replaces the built-in shockwave and dust when the ground stomp lands. Spawned at his feet.")]
        [SerializeField] private GameObject stompEffect;
        [Tooltip("Optional. Replaces the built-in charge (glow and sparks). Lives at the head while the stun charges.")]
        [SerializeField] private GameObject stunChargeEffect;
        [Tooltip("Optional. Replaces the built-in flash and ring when the stun fires (the beam still fires). Spawned at the head.")]
        [SerializeField] private GameObject stunBurstEffect;

        [Header("Start")]
        [Tooltip("Starts hunting at once. Off = he waits until Wake is called (by the BossArena).")]
        [SerializeField] private bool awakeAtStart;

        [Header("Normal form")]
        [SerializeField] private float normalSpeed = 5.5f;
        [SerializeField] private float normalTurnSpeed = 130f;
        [SerializeField] private Vector2 normalSeconds = new(20f, 30f);

        [Header("Quadruped form")]
        [SerializeField] private float quadSpeed = 12f;
        [SerializeField] private float quadTurnSpeed = 220f;
        [SerializeField] private Vector2 quadSeconds = new(10f, 16f);
        [Tooltip("Attack animations play this much faster.")]
        [SerializeField] private float quadAttackSpeed = 1.3f;
        [Tooltip("Cooldowns are multiplied by this (0.6 = attacks 40% more often).")]
        [SerializeField, Range(0.2f, 1f)] private float quadCooldownScale = 0.6f;
        [Tooltip("In normal form, if the player stays farther away than this for Far Seconds, he drops to four legs to catch up.")]
        [SerializeField] private float farDistance = 35f;
        [SerializeField] private float farSeconds = 4f;

        [Header("Movement")]
        [Tooltip("He walks until this close, then attacks. Must be smaller than the shortest melee Max Range, or he stands still.")]
        [SerializeField] private float approachDistance = 3.5f;

        [Header("Attacks")]
        [Tooltip("Stops tracking the player this far into the wind-up (0.7 = the last 30% is locked, so you can dodge).")]
        [SerializeField, Range(0.2f, 1f)] private float trackUntil = 0.7f;
        [Tooltip("The hit lands only if the player is in front of him (1 = dead ahead, 0 = to the side).")]
        [SerializeField, Range(-1f, 1f)] private float meleeArc = 0.1f;
        [SerializeField] private List<Attack> attacks = DefaultAttacks();

        [Header("Stun attack (look at the head)")]
        [Tooltip("How far from the screen centre the head can be and still count as looked at, in degrees.")]
        [SerializeField, Range(5f, 90f)] private float lookAngle = 35f;
        [SerializeField] private float stunMaxDistance = 60f;
        [SerializeField] private float flashHoldSeconds = 0.6f;
        [SerializeField] private float flashFadeSeconds = 4.5f;
        [Tooltip("Speed after the flash, as a share of normal.")]
        [SerializeField, Range(0.05f, 1f)] private float stunnedSpeed = 0.3f;
        [SerializeField] private float stunnedSeconds = 4.5f;

        [Header("Ground stomp")]
        [Tooltip("Share of the damage at the very edge of the radius (1 = no falloff).")]
        [SerializeField, Range(0f, 1f)] private float stompEdgeDamage = 0.4f;
        [Tooltip("Screen shake for the player at the boss's feet, 0 to 1.")]
        [SerializeField, Range(0f, 1f)] private float stompShake = 0.9f;

        [Header("Animator state names")]
        [SerializeField] private string idleState = "Idle";
        [SerializeField] private string walkState = "Walk";
        [SerializeField] private string quadIdleState = "Quadruped Idle";
        [SerializeField] private string quadRunState = "Quadruped Run";
        [SerializeField] private string toQuadState = "To Quadruped";
        [SerializeField] private string toNormalState = "To Hooman";
        [SerializeField] private float crossFade = 0.15f;

        private NavMeshAgent _agent;
        private Health _health;
        private IDamageable _playerDamage;
        private Health _playerHealth;
        private PlayerStatus _playerStatus;
        private HandheldCamera _shake;
        private EventBus _bus;
        private GroundTelegraph _telegraph;
        private ChargeGlow _glow;
        private GameObject _chargeInstance;
        private LookAwayWarning _warning;
        private readonly RaycastHit[] _groundHits = new RaycastHit[8];

        private readonly Dictionary<string, float> _clipLength = new();
        private BossForm _form = BossForm.Normal;
        private bool _awake;
        private bool _busy;
        private float _formTimer;
        private float _farTimer;
        private float _nextPathTime;
        private string _playing;

        public BossForm Form => _form;
        public bool IsAwake => _awake;

        /// <summary>The default attack list, tuned for the Tree Head clips. Change it in the inspector.</summary>
        private static List<Attack> DefaultAttacks()
        {
            return new List<Attack>
            {
                new() { name = "Swing", state = "Swing", kind = BossAttackKind.Melee, form = BossForm.Normal,
                    damage = 18f, maxRange = 5f, reach = 5f, lunge = 4f, hitAt = 0.5f, cooldown = 2.2f },
                new() { name = "Swing 2", state = "Swing 2", kind = BossAttackKind.Melee, form = BossForm.Normal,
                    damage = 22f, maxRange = 5f, reach = 5f, lunge = 4f, hitAt = 0.5f, cooldown = 2.4f },
                new() { name = "Kick", state = "Kick", kind = BossAttackKind.Kick, form = BossForm.Normal,
                    damage = 20f, maxRange = 5.5f, reach = 5.5f, lunge = 3f, hitAt = 0.45f, cooldown = 3f },
                new() { name = "Strong kick", state = "Kick2", kind = BossAttackKind.StrongKick, form = BossForm.Normal,
                    damage = 28f, maxRange = 5.5f, reach = 5.5f, lunge = 3f, hitAt = 0.5f, cooldown = 8f, weight = 0.7f, knockbackOrRadius = 32f },
                // The clip called "Stomp" is the stun attack, the clip called "Ult" is the ground stomp.
                new() { name = "Stun", state = "Stomp", kind = BossAttackKind.Stun, form = BossForm.Normal,
                    minRange = 10f, maxRange = 40f, hitAt = 0.65f, cooldown = 22f, weight = 2f },
                new() { name = "Ground stomp", state = "Ult", kind = BossAttackKind.Stomp, form = BossForm.Normal,
                    damage = 40f, maxRange = 18f, hitAt = 0.55f, cooldown = 30f, weight = 1.5f, knockbackOrRadius = 20f },
                new() { name = "Quad swipe", state = "Quadruped Swing", kind = BossAttackKind.Melee, form = BossForm.Quadruped,
                    damage = 12f, maxRange = 5f, reach = 5f, lunge = 5f, hitAt = 0.5f, cooldown = 1.6f },
                new() { name = "Quad swipe 2", state = "Quadruped Swing 2", kind = BossAttackKind.Melee, form = BossForm.Quadruped,
                    damage = 14f, maxRange = 5f, reach = 5f, lunge = 5f, hitAt = 0.5f, cooldown = 1.8f }
            };
        }

        [ContextMenu("Reset attacks to the Tree Head defaults")]
        private void ResetAttacks() => attacks = DefaultAttacks();

        // ---------- setup ----------

        private const float LungeStart = 0.4f; // share of the wind-up before the lunge begins

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
            if (!animator) animator = GetComponentInChildren<Animator>();

            if (!player)
            {
                var controller = FindAnyObjectByType<PlayerController>();
                if (controller) player = controller.transform;
            }

            if (player)
            {
                _playerDamage = player.GetComponent<IDamageable>();
                _playerHealth = player.GetComponent<Health>();
                _playerStatus = PlayerStatus.Of(player.gameObject);
            }
            else
            {
                Debug.LogWarning("[BossController] No PlayerController found. The boss will stay idle.", this);
            }

            if (!head) head = FindHead();
            if (chargeLight) chargeLight.enabled = false;

            ReadClipLengths();
            WarnAboutMissingStates();

            _agent.angularSpeed = 0f; // turning is done here, so it matches the attack animations
            _agent.updateRotation = false;
            _agent.stoppingDistance = 0f;
            _agent.isStopped = true;
        }

        private void OnEnable()
        {
            GameServices.TryGet(out _bus);
        }

        private void OnDisable()
        {
            CleanupVfx();
            StopFire();
        }

        private void Start()
        {
            if (awakeAtStart) Wake();
            else PlayLocomotion(true);
        }

        // ---------- the encounter ----------

        /// <summary>Starts hunting the player. Called by the arena, or by awakeAtStart.</summary>
        public void Wake()
        {
            if (_awake) return;

            _awake = true;
            SetForm(BossForm.Normal);
            _formTimer = UnityEngine.Random.Range(normalSeconds.x, normalSeconds.y);
            _agent.isStopped = false;
        }

        /// <summary>Stops the fight: he stands still. The arena calls this when the encounter ends.</summary>
        public void Sleep()
        {
            _awake = false;
            StopAllCoroutines();
            _busy = false;
            CleanupVfx();
            if (chargeLight) chargeLight.enabled = false;
            if (animator) animator.speed = 1f;
            if (_agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
            PlayLocomotion(true);
        }

        // ---------- every frame ----------

        private void Update()
        {
            if (!_awake || !player || Time.deltaTime <= 0f) return;

            if (_playerHealth && _playerHealth.IsDead)
            {
                if (!_busy) PlayLocomotion(true);
                _agent.isStopped = true;
                return;
            }

            if (_busy) return;

            var distance = FlatDistance();

            // Form changes: on a timer, or early when the player keeps far away.
            _formTimer -= Time.deltaTime;
            _farTimer = _form == BossForm.Normal && distance > farDistance ? _farTimer + Time.deltaTime : 0f;

            if (_formTimer <= 0f || _farTimer >= farSeconds)
            {
                StartCoroutine(ChangeForm(_form == BossForm.Normal ? BossForm.Quadruped : BossForm.Normal));
                return;
            }

            var attack = PickAttack(distance);
            if (attack != null)
            {
                StartCoroutine(DoAttack(attack));
                return;
            }

            Chase(distance);
        }

        // ---------- fire ----------

        private float _burnUntil;
        private GameObject _fireObject;
        private Light _fireLight;
        private GameObject _fireEffect;
        private float _fireSeed;

        /// <summary>
        /// Incendiary rounds set him alight for a few seconds; every hit refreshes the time. It is only a show: he cannot
        /// be killed, so the fire does no damage. Uses the camera's Burn Effect Prefab, like the zombies do.
        /// </summary>
        public void Ignite(float seconds)
        {
            if (seconds <= 0f || !isActiveAndEnabled) return;

            _burnUntil = Time.time + seconds;
            if (_fireObject) return;

            _fireSeed = UnityEngine.Random.value * 100f;
            var centre = BodyCentre();

            _fireObject = new GameObject("BossFire");
            _fireObject.transform.SetParent(transform, true);
            _fireObject.transform.position = centre;
            _fireLight = _fireObject.AddComponent<Light>();
            _fireLight.type = LightType.Point;
            _fireLight.color = new Color(1f, 0.55f, 0.15f);
            _fireLight.range = fireLightRange;
            _fireLight.intensity = 4f;

            var prefab = EnemyStatus.SharedBurnEffect;
            if (!prefab) return;

            _fireEffect = Instantiate(prefab, transform);
            _fireEffect.transform.localScale = Vector3.one * (EnemyStatus.SharedBurnScale * fireScale);
            _fireEffect.transform.position = centre;
            _fireEffect.transform.rotation = prefab.transform.rotation;
        }

        private Vector3 BodyCentre()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            var found = false;
            var bounds = new Bounds(transform.position + Vector3.up * 2f, Vector3.zero);

            foreach (var renderer in renderers)
            {
                if (renderer is ParticleSystemRenderer || renderer is LineRenderer) continue; // his own effects are not his body

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds.center;
        }

        private void UpdateFire()
        {
            if (!_fireObject) return;

            if (Time.time >= _burnUntil)
            {
                StopFire();
                return;
            }

            _fireLight.intensity = 3.5f + Mathf.PerlinNoise(_fireSeed, Time.time * 9f) * 3f;
        }

        private void StopFire()
        {
            if (_fireObject) Destroy(_fireObject);
            _fireObject = null;
            _fireLight = null;

            if (!_fireEffect) return;

            // The flames stop emitting and fade out, then the object goes.
            foreach (var system in _fireEffect.GetComponentsInChildren<ParticleSystem>())
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            Destroy(_fireEffect, 4f);
            _fireEffect = null;
        }

        private void LateUpdate()
        {
            UpdateFire();

            // He cannot die: whatever the player's weapons did, he is back to full health.
            if (_health && !_health.IsDead && _health.Current < _health.Max) _health.Heal(_health.Max);
        }

        private void Chase(float distance)
        {
            if (!_agent.enabled || !_agent.isOnNavMesh) return;

            if (distance <= approachDistance)
            {
                _agent.isStopped = true;
                FaceTarget(Time.deltaTime, TurnSpeed);
                PlayLocomotion(true);
                return;
            }

            _agent.isStopped = false;
            _agent.speed = _form == BossForm.Normal ? normalSpeed : quadSpeed;

            if (Time.time >= _nextPathTime)
            {
                _nextPathTime = Time.time + 0.15f;
                _agent.SetDestination(player.position);
            }

            // The agent does not rotate (Angular Speed 0), so turn towards where it is going.
            var velocity = _agent.velocity;
            velocity.y = 0f;
            if (velocity.sqrMagnitude > 0.25f) TurnTowards(velocity, Time.deltaTime, TurnSpeed);

            PlayLocomotion(velocity.sqrMagnitude < 0.25f);
        }

        // ---------- attacks ----------

        private Attack PickAttack(float distance)
        {
            Attack picked = null;
            var total = 0f;

            foreach (var attack in attacks)
            {
                if (attack.form != _form || Time.time < attack.NextTime) continue;
                if (distance < attack.minRange || distance > attack.maxRange) continue;

                total += attack.weight;
                if (UnityEngine.Random.value * total <= attack.weight) picked = attack;
            }

            return picked;
        }

        private IEnumerator DoAttack(Attack attack)
        {
            _busy = true;
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;

            var speed = _form == BossForm.Quadruped ? quadAttackSpeed : 1f;
            animator.speed = speed;
            Play(attack.state);

            var length = Length(attack.state) / speed;
            var hitTime = length * attack.hitAt;
            var time = 0f;

            // The lunge: he steps forward along the way he faces, in the second half of the wind-up (so he does not
            // slide early). Straight ahead, not tracking, so a sidestep dodges it while a step back does not.
            var lungeFrom = hitTime * LungeStart;
            var lungeSpeed = attack.lunge / Mathf.Max(0.05f, hitTime - lungeFrom);
            var lungeLeft = attack.lunge;

            _telegraph = StartTelegraph(attack, hitTime);
            if (attack.kind == BossAttackKind.Stun) BeginCharge();

            while (time < hitTime)
            {
                time += Time.deltaTime;

                if (time < hitTime * trackUntil) FaceTarget(Time.deltaTime, TurnSpeed);
                if (attack.kind == BossAttackKind.Stun) SetCharge(Mathf.Clamp01(time / hitTime));

                if (lungeLeft > 0f && time >= lungeFrom)
                {
                    var planned = Mathf.Min(lungeSpeed * Time.deltaTime, lungeLeft);
                    LungeStep(planned);
                    lungeLeft -= planned;
                }

                yield return null;
            }

            Land(attack);
            EndCharge();
            if (chargeLight) chargeLight.enabled = false;

            // After the hit he stays in the animation for the rest of the clip. The stun is the exception: he should
            // start walking at the stunned player at once, so the stun is worth something.
            var recovery = attack.recovery >= 0f
                ? attack.recovery
                : attack.kind == BossAttackKind.Stun ? 0.25f : length - hitTime;
            yield return new WaitForSeconds(Mathf.Max(0.05f, recovery));

            attack.NextTime = Time.time + attack.cooldown * (_form == BossForm.Quadruped ? quadCooldownScale : 1f);
            animator.speed = 1f;
            _playing = null;
            _busy = false;
        }

        /// <summary>Moves him forward, but never into the player (he stops about a body's width away).</summary>
        private void LungeStep(float distance)
        {
            if (!_agent.enabled || !_agent.isOnNavMesh) return;

            var forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) return;

            var gap = FlatDistance() - (_agent.radius + 1.2f);
            var step = Mathf.Min(distance, gap);
            if (step > 0f) _agent.Move(forward.normalized * step);
        }

        private static float ReachOf(Attack attack) => attack.reach > 0f ? attack.reach : attack.maxRange;

        /// <summary>The moment the attack hits.</summary>
        private void Land(Attack attack)
        {
            if (!player) return;

            switch (attack.kind)
            {
                case BossAttackKind.Melee:
                case BossAttackKind.Kick:
                    if (InReach(ReachOf(attack))) Hurt(attack.damage);
                    break;

                case BossAttackKind.StrongKick:
                    if (InReach(ReachOf(attack)))
                    {
                        Hurt(attack.damage);

                        var away = player.position - transform.position;
                        away.y = 0f;
                        if (_playerStatus) _playerStatus.Knockback(away.normalized * attack.knockbackOrRadius);
                    }
                    break;

                case BossAttackKind.Stun:
                    StunShot();
                    if (PlayerLooksAtHead() && _playerStatus)
                    {
                        _playerStatus.Flash(flashHoldSeconds, flashFadeSeconds);
                        _playerStatus.Slow(stunnedSpeed, stunnedSeconds);
                    }
                    break;

                case BossAttackKind.Stomp:
                    Stomp(attack);
                    break;
            }
        }

        private void Stomp(Attack attack)
        {
            var radius = Mathf.Max(1f, attack.knockbackOrRadius);
            var distance = FlatDistance();

            if (stompEffect) Instantiate(stompEffect, transform.position, Quaternion.identity);
            else BossVfx.Shockwave(new Vector3(transform.position.x, GroundY(), transform.position.z), radius, effectScale);

            if (distance <= radius)
            {
                var falloff = Mathf.Lerp(1f, stompEdgeDamage, distance / radius);
                Hurt(attack.damage * falloff);
            }

            // Everyone close enough feels it, hit or not.
            if (!_shake) _shake = FindAnyObjectByType<HandheldCamera>();
            if (_shake) _shake.AddTrauma(stompShake * Mathf.Clamp01(1.5f - distance / (radius * 2f)));
        }

        private void Hurt(float damage)
        {
            if (_playerDamage != null && damage > 0f) _playerDamage.TakeDamage(damage, gameObject);
        }

        private bool InReach(float range)
        {
            if (FlatDistance() > range) return false;

            var toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            return Vector3.Dot(transform.forward, toPlayer.normalized) >= meleeArc;
        }

        // ---------- the stun attack ----------

        private void SetCharge(float amount)
        {
            if (_glow) _glow.SetAmount(amount);
            var looking = PlayerLooksAtHead();
            if (_warning) _warning.Show(lookAwayWarning && looking);

            // The player's screen whitens from the edges while they look at his head. Looking away clears it again.
            if (_playerStatus) _playerStatus.SetBlind(looking ? amount : 0f);

            if (!chargeLight) return;

            chargeLight.enabled = true;
            chargeLight.intensity = chargeLightIntensity * amount * amount; // slow at first, fierce at the end
        }

        // ---------- attack visuals ----------

        /// <summary>The warning on the floor for attacks that hit the ground around or in front of him.</summary>
        private GroundTelegraph StartTelegraph(Attack attack, float hitTime)
        {
            if (!showTelegraphs) return null;

            float radius;
            float halfAngle;
            Color color;

            switch (attack.kind)
            {
                case BossAttackKind.Stomp:
                    radius = Mathf.Max(1f, attack.knockbackOrRadius);
                    halfAngle = 180f;
                    color = new Color(1f, 0.12f, 0.1f);
                    break;
                case BossAttackKind.StrongKick:
                    radius = ReachOf(attack) + attack.lunge; // where the hit can land, counting the lunge
                    halfAngle = ArcDegrees();
                    color = new Color(1f, 0.7f, 0.1f);
                    break;
                case BossAttackKind.Melee:
                case BossAttackKind.Kick:
                    radius = ReachOf(attack) + attack.lunge;
                    halfAngle = ArcDegrees();
                    color = new Color(1f, 0.4f, 0.1f);
                    break;
                default:
                    return null; // the stun has its own charge
            }

            // With a lunge the warning stays where he started (he moves out of it); without one it follows him.
            return GroundTelegraph.Create(transform, radius, halfAngle, color, hitTime, trackUntil,
                GroundY() + telegraphHeight, attack.lunge <= 0f);
        }

        /// <summary>The same angle InReach uses: a hit lands when the player is within this many degrees of straight ahead.</summary>
        private float ArcDegrees()
        {
            return Mathf.Clamp(Mathf.Acos(Mathf.Clamp(meleeArc, -1f, 1f)) * Mathf.Rad2Deg, 10f, 180f);
        }

        private void BeginCharge()
        {
            if (stunChargeEffect)
            {
                _chargeInstance = Instantiate(stunChargeEffect, HeadPosition, Quaternion.identity);
                _chargeInstance.AddComponent<FollowPoint>().Target = head;
            }
            else
            {
                _glow = ChargeGlow.Create(head, HeadPosition, effectScale);
            }

            if (lookAwayWarning && !_warning) _warning = LookAwayWarning.Create(transform);
        }

        private void EndCharge()
        {
            if (_glow) Destroy(_glow.gameObject);
            if (_chargeInstance) Destroy(_chargeInstance);
            if (_warning) _warning.Show(false);
            if (_playerStatus) _playerStatus.SetBlind(0f); // a flash that just fired keeps going on its own
            _glow = null;
            _chargeInstance = null;
        }

        /// <summary>The stun fires: flash, ring and a beam to the player (or your own prefab instead).</summary>
        private void StunShot()
        {
            var cam = Camera.main;
            var target = cam ? cam.transform.position - Vector3.up * 0.4f : player.position + Vector3.up * 3f;

            // The beam always fires. Your prefab replaces only the flash and ring at the head.
            BossVfx.StunBeam(HeadPosition, target, effectScale);

            if (stunBurstEffect) Instantiate(stunBurstEffect, HeadPosition, Quaternion.identity);
            else BossVfx.StunFlash(HeadPosition, effectScale);
        }

        /// <summary>Removes any attack visual still running (the fight ended mid attack).</summary>
        private void CleanupVfx()
        {
            if (_telegraph) Destroy(_telegraph.gameObject);
            _telegraph = null;
            EndCharge();
        }

        /// <summary>The floor under him, ignoring his own body and the player.</summary>
        private float GroundY()
        {
            var origin = transform.position + Vector3.up * 2f;
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, 40f, ~0, QueryTriggerInteraction.Ignore);

            var best = float.MaxValue;
            var y = transform.position.y;
            for (var i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                var other = hit.collider.transform;
                if (other.IsChildOf(transform)) continue;
                if (player && other.IsChildOf(player.root)) continue;

                if (hit.distance < best)
                {
                    best = hit.distance;
                    y = hit.point.y;
                }
            }

            return y;
        }

        private Vector3 HeadPosition => head ? head.position : transform.position + Vector3.up * headHeight;

        /// <summary>True when the head is near the screen centre, close enough, and not hidden behind something.</summary>
        private bool PlayerLooksAtHead()
        {
            var cam = Camera.main;
            if (!cam) return false;

            var origin = cam.transform.position;
            var target = HeadPosition;
            var toHead = target - origin;

            if (toHead.magnitude > stunMaxDistance) return false;
            if (Vector3.Angle(cam.transform.forward, toHead) > lookAngle) return false;

            // Something solid between them blocks it. The boss's own body and the player do not count.
            if (Physics.Linecast(origin, target, out var hit, ~0, QueryTriggerInteraction.Ignore))
            {
                var other = hit.collider.transform;
                if (!other.IsChildOf(transform) && !other.IsChildOf(player.root)) return false;
            }

            return true;
        }

        // ---------- forms ----------

        private IEnumerator ChangeForm(BossForm to)
        {
            _busy = true;
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
            animator.speed = 1f;

            var state = to == BossForm.Quadruped ? toQuadState : toNormalState;
            Play(state);
            yield return new WaitForSeconds(Length(state));

            SetForm(to);
            _formTimer = to == BossForm.Normal
                ? UnityEngine.Random.Range(normalSeconds.x, normalSeconds.y)
                : UnityEngine.Random.Range(quadSeconds.x, quadSeconds.y);
            _farTimer = 0f;
            _playing = null;
            _busy = false;
        }

        private void SetForm(BossForm form)
        {
            _form = form;
            _agent.speed = form == BossForm.Normal ? normalSpeed : quadSpeed;

            if (_bus != null || GameServices.TryGet(out _bus)) _bus.Publish(new BossFormEvent(form == BossForm.Quadruped));
        }

        // ---------- turning and distance ----------

        private float TurnSpeed => _form == BossForm.Normal ? normalTurnSpeed : quadTurnSpeed;

        private float FlatDistance()
        {
            var d = player.position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        private void FaceTarget(float dt, float degreesPerSecond)
        {
            var toPlayer = player.position - transform.position;
            TurnTowards(toPlayer, dt, degreesPerSecond);
        }

        private void TurnTowards(Vector3 direction, float dt, float degreesPerSecond)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degreesPerSecond * dt);
        }

        // ---------- animation ----------

        private void PlayLocomotion(bool standing)
        {
            if (_form == BossForm.Normal) Play(standing ? idleState : walkState);
            else Play(standing ? quadIdleState : quadRunState);
        }

        /// <summary>Plays an Animator state by name. Does nothing if it is already playing.</summary>
        private void Play(string state)
        {
            if (!animator || state == _playing) return;

            _playing = state;
            animator.CrossFadeInFixedTime(state, crossFade);
        }

        private float Length(string state)
        {
            return _clipLength.TryGetValue(state, out var length) && length > 0.05f ? length : 1.5f;
        }

        /// <summary>The clip lengths, by the name after the | (the FBX names clips "rig|Swing").</summary>
        private void ReadClipLengths()
        {
            if (!animator || !animator.runtimeAnimatorController) return;

            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                var name = clip.name;
                var bar = name.LastIndexOf('|');
                if (bar >= 0) name = name.Substring(bar + 1);

                _clipLength[name.Trim()] = clip.length;
            }
        }

        private void WarnAboutMissingStates()
        {
            if (!animator) return;

            var names = new List<string> { idleState, walkState, quadIdleState, quadRunState, toQuadState, toNormalState };
            foreach (var attack in attacks) names.Add(attack.state);

            foreach (var name in names)
            {
                if (!_clipLength.ContainsKey(name))
                {
                    Debug.LogWarning($"[BossController] No animation clip called \"{name}\" in the Animator. " +
                                     "Fix the state name in the inspector, or rebuild the controller.", this);
                }
            }
        }

        /// <summary>Finds a bone called Head. The armature is often named "...head...", so only exact bone names count.</summary>
        private Transform FindHead()
        {
            if (animator && animator.isHuman)
            {
                var bone = animator.GetBoneTransform(HumanBodyBones.Head);
                if (bone) return bone;
            }

            foreach (var t in GetComponentsInChildren<Transform>())
            {
                var n = t.name.ToLowerInvariant();
                if (n == "head" || n == "head_jnt" || n.EndsWith(":head") || n.EndsWith("_head") || n.EndsWith(".head")) return t;
            }

            return null;
        }
    }
}