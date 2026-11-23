using System;
using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// The player's heart rate, and what it does to the aim. Put it on the Player, or let WeaponController and the HUD add it.
    ///
    /// The heart rate follows the state of the body: it rises as health drops, jumps for several seconds after every hit
    /// (adrenaline), rises while sprinting and a little when drunk. It speeds up fast and calms down slowly.
    /// The HUD draws it as the pulse trace. The pistol reads it as extra bullet spread: calm hands shoot straight, a racing
    /// heart does not. Aiming down the sights steadies it (Aim Multiplier).
    ///
    /// ExtraSpreadDegrees is quadratic in how far the rate is between Calm Bpm and Panic Bpm, so walking about and
    /// sprinting cost almost nothing, a hit costs a little for a few seconds, and being nearly dead costs a lot.
    /// </summary>
    public class PlayerPulse : MonoBehaviour
    {
        public static PlayerPulse Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [Header("References (empty = found automatically)")]
        [SerializeField] private Health health;
        [SerializeField] private PlayerStamina stamina;

        [Header("Heart rate (beats per minute)")]
        [SerializeField] private float restBpm = 64f;
        [Tooltip("The rate at zero health, before the other effects.")]
        [SerializeField] private float lowHealthBpm = 138f;
        [Tooltip("Added right after taking a lot of damage. Fades over about seven seconds.")]
        [SerializeField] private float adrenalineBpm = 38f;
        [SerializeField] private float sprintBpm = 16f;
        [SerializeField] private float drunkBpm = 6f;
        [Tooltip("How fast the rate climbs and how fast it comes back down (per second, bigger = faster).")]
        [SerializeField] private float riseSpeed = 5f;
        [SerializeField] private float fallSpeed = 1.2f;

        [Header("Bullet spread")]
        [Tooltip("At or below this rate there is no extra spread.")]
        [SerializeField] private float calmBpm = 70f;
        [Tooltip("At or above this rate the extra spread is at its maximum.")]
        [SerializeField] private float panicBpm = 150f;
        [Tooltip("Degrees of extra spread, in every direction, at the panic rate. 0 turns the effect off.")]
        [SerializeField, Min(0f)] private float maxExtraSpreadDegrees = 2.5f;
        [Tooltip("Fraction of the extra spread left while aiming (0.5 = half).")]
        [SerializeField, Range(0f, 1f)] private float aimMultiplier = 0.5f;

        private float _bpm;
        private float _adrenaline;
        private float _previousHealth = -1f;

        /// <summary>Smoothed heart rate. 0 when dead.</summary>
        public float Bpm => _bpm;

        /// <summary>0 at the calm rate, 1 at the panic rate.</summary>
        public float Excitement01 => Mathf.InverseLerp(calmBpm, panicBpm, _bpm);

        /// <summary>Raised when health drops. The value is the damage as a fraction of max health.</summary>
        public event Action<float> Hit;

        public static PlayerPulse Of(GameObject player)
        {
            if (Instance) return Instance;

            var pulse = player.GetComponent<PlayerPulse>();
            return pulse ? pulse : player.AddComponent<PlayerPulse>();
        }

        /// <summary>Extra bullet spread in degrees from the heart rate.</summary>
        public float ExtraSpreadDegrees(bool aiming)
        {
            var e = Excitement01;
            return e * e * maxExtraSpreadDegrees * (aiming ? aimMultiplier : 1f);
        }

        private void Awake()
        {
            Instance = this;
            _bpm = restBpm;

            if (!health) health = GetComponentInChildren<Health>();
            if (!health) health = GetComponentInParent<Health>();
            if (!stamina) stamina = GetComponentInChildren<PlayerStamina>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var dt = Time.deltaTime; // 0 while the suitcase is open, so nothing moves then
            if (dt <= 0f) return;

            var fraction = 1f;
            var dead = false;

            if (health)
            {
                fraction = Mathf.Clamp01(health.Current / Mathf.Max(1f, health.Max));
                dead = health.IsDead;

                if (_previousHealth >= 0f)
                {
                    var lost = (_previousHealth - health.Current) / Mathf.Max(1f, health.Max);
                    if (lost > 0.0005f)
                    {
                        _adrenaline = Mathf.Clamp01(_adrenaline + lost * 2.5f);
                        Hit?.Invoke(lost);
                    }
                }

                _previousHealth = health.Current;
            }

            _adrenaline = Mathf.MoveTowards(_adrenaline, 0f, dt * 0.15f);

            var buffs = PlayerBuffs.Instance;
            var target = Mathf.Lerp(lowHealthBpm, restBpm, fraction) + _adrenaline * adrenalineBpm;
            if (stamina && stamina.IsSprinting) target += sprintBpm;
            if (buffs) target += buffs.Drunk * drunkBpm;
            if (dead) target = 0f;

            _bpm = Mathf.Lerp(_bpm, target, 1f - Mathf.Exp(-(target > _bpm ? riseSpeed : fallSpeed) * dt));
            if (dead && _bpm < 1f) _bpm = 0f;
        }
    }
}