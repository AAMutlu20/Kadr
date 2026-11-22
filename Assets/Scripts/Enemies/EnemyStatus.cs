using Gameplay;
using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// Temporary effects from the camera flash on one enemy: stun, burning, disorient. Added automatically to an
    /// enemy by the camera the first time it is hit by an ability, so no prefab needs setting up.
    ///
    ///  - Stun: the enemy freezes (EnemyController.SetStunned). Bosses can take less time, see EnemyStats.
    ///  - Burning: damage every second until it runs out. The damage comes from the player, so a kill pays XP as usual.
    ///  - Disorient: the enemy fights the nearest other enemy (preferring others that are also disoriented), and goes
    ///    back to the player when the time is up. With "until dead" it fights until it dies. With no other enemy around
    ///    nothing changes.
    ///
    /// Everything uses game time, so it stops while the inventory is open.
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public class EnemyStatus : MonoBehaviour
    {
        private const float RetargetInterval = 0.5f;
        private const float MaxFightDistance = 60f;

        private EnemyController _enemy;
        private Health _health;

        private float _stunUntil;

        private bool _burning;
        private float _burnUntil;
        private float _nextTick;
        private float _burnDamage;
        private GameObject _burnSource;
        private GameObject _fire;
        private GameObject _fireEffect;

        private bool _disoriented;
        private bool _disorientForever;
        private float _disorientUntil;
        private float _nextRetarget;

        public bool IsStunned => _enemy && _enemy.Stunned;
        public bool IsBurning => _burning;
        public bool IsDisoriented => _disoriented;

        private void Awake()
        {
            _enemy = GetComponent<EnemyController>();
            _health = GetComponent<Health>();
            _health.Died += ClearAll;
        }

        private void OnDestroy()
        {
            if (_health) _health.Died -= ClearAll;
        }

        // ---------- applying ----------

        public void Stun(float seconds)
        {
            if (_health.IsDead || !_enemy.Stats) return;

            seconds *= _enemy.Stats.flashStunScale;
            if (seconds <= 0f) return;

            _stunUntil = Mathf.Max(_stunUntil, Time.time + seconds);
            _enemy.SetStunned(true);
        }

        /// <summary>
        /// The fire particle system used when Burn is called without its own prefab (incendiary bullets). The photo
        /// camera sets it from its Burn Effect Prefab field, so the whole game uses one fire.
        /// </summary>
        public static GameObject SharedBurnEffect;
        public static float SharedBurnScale = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetShared()
        {
            SharedBurnEffect = null;
            SharedBurnScale = 1f;
        }

        /// <summary>effectPrefab: your fire particle system (optional). It is placed in the middle of the enemy's body.</summary>
        public void Burn(float seconds, float damagePerSecond, GameObject source, GameObject effectPrefab = null, float effectScale = 1f)
        {
            if (_health.IsDead || !_enemy.Stats || _enemy.Stats.immuneToFire || seconds <= 0f) return;

            _burnUntil = Time.time + seconds; // burning again refreshes the time, it does not stack
            _burnDamage = damagePerSecond;
            _burnSource = source;

            if (_burning) return;

            if (!effectPrefab)
            {
                effectPrefab = SharedBurnEffect;
                effectScale = SharedBurnScale;
            }

            _burning = true;
            _nextTick = Time.time + 1f;
            MakeFire();
            MakeEffect(effectPrefab, effectScale);
        }

        /// <summary>seconds 0 or less = until the enemy dies.</summary>
        public void Disorient(float seconds)
        {
            if (_health.IsDead || !_enemy.Stats || _enemy.Stats.immuneToDisorient) return;

            _disoriented = true;
            _disorientForever = seconds <= 0f;
            _disorientUntil = Time.time + seconds;
            _nextRetarget = 0f;
        }

        // ---------- every frame ----------

        private void Update()
        {
            if (_health.IsDead) return;

            if (_enemy.Stunned && Time.time >= _stunUntil) _enemy.SetStunned(false);

            if (_burning) UpdateBurn();
            if (_health.IsDead) return; // the fire may have killed it

            if (_disoriented) UpdateDisorient();
        }

        private void UpdateBurn()
        {
            if (Time.time >= _nextTick)
            {
                _nextTick += 1f;
                _health.TakeDamage(_burnDamage, _burnSource);
                if (_health.IsDead) return;
            }

            if (Time.time >= _burnUntil) StopBurning();
        }

        private void UpdateDisorient()
        {
            if (!_disorientForever && Time.time >= _disorientUntil)
            {
                _disoriented = false;
                _enemy.ResetTarget();
                return;
            }

            if (Time.time < _nextRetarget) return;
            _nextRetarget = Time.time + RetargetInterval;

            // Still fighting someone alive? Nothing to do.
            if (_enemy.TargetIsEnemy && _enemy.HasTarget) return;

            var victim = FindVictim();
            if (victim) _enemy.SetTargetOverride(victim.transform);
            else _enemy.ResetTarget(); // nobody left (or the old victim is gone): back to the player
        }

        private EnemyController FindVictim()
        {
            EnemyController best = null;
            var bestScore = float.MaxValue;

            foreach (var other in EnemyController.All)
            {
                if (!other || other == _enemy || other.IsDead) continue;

                var distance = Vector3.Distance(transform.position, other.transform.position);
                if (distance > MaxFightDistance) continue;

                // Other disoriented enemies count as closer, so a group splits up into fights.
                var score = other.TryGetComponent<EnemyStatus>(out var status) && status.IsDisoriented ? distance * 0.5f : distance;
                if (score >= bestScore) continue;

                bestScore = score;
                best = other;
            }

            return best;
        }

        // ---------- ending ----------

        private void StopBurning()
        {
            _burning = false;
            if (_fire) Destroy(_fire);

            // The particles stop emitting and fade out by themselves, then the object goes.
            if (_fireEffect)
            {
                foreach (var system in _fireEffect.GetComponentsInChildren<ParticleSystem>())
                {
                    system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }

                Destroy(_fireEffect, 4f);
                _fireEffect = null;
            }
        }

        private void MakeEffect(GameObject prefab, float scale)
        {
            if (!prefab) return;

            _fireEffect = Instantiate(prefab, transform);
            _fireEffect.transform.localScale = Vector3.one * scale;

            // Middle of the body, wherever the model's origin is.
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                _fireEffect.transform.position = bounds.center;
            }
            else
            {
                _fireEffect.transform.localPosition = Vector3.up * 1.5f;
            }

            _fireEffect.transform.rotation = prefab.transform.rotation; // keeps the prefab's own rotation (the menu's -90 on X), so flames rise whichever way the enemy faces
        }

        private void ClearAll()
        {
            _disoriented = false;
            StopBurning();
            _enemy.SetStunned(false);
        }

        private void MakeFire()
        {
            // A flickering orange light, so a burning enemy lights up its surroundings. Swap in a particle prefab later.
            _fire = new GameObject("Fire");
            _fire.transform.SetParent(transform, false);
            _fire.transform.localPosition = Vector3.up * 1.5f;

            var light = _fire.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.15f);
            light.range = 9f;
            light.intensity = 3f;
            _fire.AddComponent<FireFlicker>();
        }

        private sealed class FireFlicker : MonoBehaviour
        {
            private Light _light;
            private float _seed;

            private void Awake()
            {
                _light = GetComponent<Light>();
                _seed = Random.value * 100f;
            }

            private void Update()
            {
                _light.intensity = 2.5f + Mathf.PerlinNoise(_seed, Time.time * 9f) * 2f;
            }
        }
    }
}