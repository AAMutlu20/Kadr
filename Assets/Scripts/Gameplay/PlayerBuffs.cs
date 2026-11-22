using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// Temporary effects on the player from consumables. Added to the Player automatically the first time an item
    /// needs it (PlayerBuffs.Of), so there is nothing to set up. Other scripts only READ it:
    ///
    ///   Infinite stamina (Leninade)   PlayerStamina stops draining for a while.
    ///   Alcohol                       takes damage down (Health.DamageTakenMultiplier) for a while, and makes the
    ///                                 player drunk: more camera wobble (HandheldCamera), faster stamina drain and
    ///                                 slower refill (PlayerStamina), wider bullet spread (WeaponBase).
    ///
    /// Drunkenness fades in and out smoothly instead of switching, and it outlasts the damage resistance a little.
    /// Time.deltaTime is 0 while the suitcase is open, so the timers do not run then.
    /// </summary>
    public class PlayerBuffs : MonoBehaviour
    {
        /// <summary>The player's buffs, or null before the first item that needs them was used.</summary>
        public static PlayerBuffs Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [Header("Drunk, at full strength (1.0)")]
        [Tooltip("Extra bullet spread in degrees, added to the weapon's own (also while aiming).")]
        [SerializeField] private float extraSpreadDegrees = 1.5f;
        [Tooltip("Stamina drains this many times faster.")]
        [SerializeField] private float staminaDrainMultiplier = 1.4f;
        [Tooltip("Stamina refills at this fraction of the normal speed.")]
        [SerializeField, Range(0.1f, 1f)] private float staminaRegenMultiplier = 0.7f;
        [Tooltip("Seconds the drunkenness lingers after the damage resistance has run out.")]
        [SerializeField] private float soberUpSeconds = 8f;

        private Health _health;
        private float _infiniteLeft;
        private float _infiniteTotal = 1f;
        private float _resistLeft;
        private float _resistance = 1f;
        private float _drunkTarget;
        private float _drunkLevel;
        private float _appliedMultiplier = 1f;
        private float _soberLeft;

        public bool InfiniteStamina => _infiniteLeft > 0f;
        /// <summary>1 when the infinite stamina has just started, down to 0 when it ends. For the bar.</summary>
        public float InfiniteFraction => _infiniteTotal > 0f ? Mathf.Clamp01(_infiniteLeft / _infiniteTotal) : 0f;

        /// <summary>0 sober .. 1 as drunk as it gets.</summary>
        public float Drunk => _drunkLevel;
        public bool IsDrunk => _drunkLevel > 0.01f;

        public float DamageTakenMultiplier => _resistLeft > 0f ? _resistance : 1f;
        public float ExtraSpreadDegrees => extraSpreadDegrees * _drunkLevel;
        public float StaminaDrainMultiplier => Mathf.Lerp(1f, staminaDrainMultiplier, _drunkLevel);
        public float StaminaRegenMultiplier => Mathf.Lerp(1f, staminaRegenMultiplier, _drunkLevel);

        public static PlayerBuffs Of(GameObject player)
        {
            var buffs = player.GetComponent<PlayerBuffs>();
            return buffs ? buffs : player.AddComponent<PlayerBuffs>();
        }

        /// <summary>Leninade. Starting it again only refreshes the time.</summary>
        public void GiveInfiniteStamina(float seconds)
        {
            _infiniteLeft = seconds;
            _infiniteTotal = Mathf.Max(0.1f, seconds);
        }

        /// <summary>
        /// Alcohol. Damage is multiplied by damageMultiplier (0.6 = 40% less) for the given seconds. Strength adds to how
        /// drunk the player is (capped at 1), so a second drink makes it worse. Drinking again refreshes the time.
        /// </summary>
        public void Drink(float seconds, float damageMultiplier, float strength)
        {
            _resistance = _resistLeft > 0f ? Mathf.Min(_resistance, damageMultiplier) : damageMultiplier;
            _resistLeft = Mathf.Max(_resistLeft, seconds);
            _drunkTarget = Mathf.Clamp01(_drunkTarget + strength);
            _soberLeft = Mathf.Max(_soberLeft, seconds + soberUpSeconds);
        }

        private void Awake()
        {
            Instance = this;
            _health = GetComponent<Health>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDisable()
        {
            ApplyResistance(1f);
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_infiniteLeft > 0f) _infiniteLeft = Mathf.Max(0f, _infiniteLeft - dt);

            if (_resistLeft > 0f)
            {
                _resistLeft = Mathf.Max(0f, _resistLeft - dt);
                if (_resistLeft <= 0f) _resistance = 1f;
            }

            if (_soberLeft > 0f)
            {
                _soberLeft = Mathf.Max(0f, _soberLeft - dt);
                if (_soberLeft <= 0f) _drunkTarget = 0f;
            }

            // Eases in over a couple of seconds, out over about five.
            var speed = _drunkLevel < _drunkTarget ? 0.5f : 0.2f;
            _drunkLevel = Mathf.MoveTowards(_drunkLevel, _drunkTarget, speed * dt);

            ApplyResistance(DamageTakenMultiplier);
        }

        private void ApplyResistance(float multiplier)
        {
            if (!_health || Mathf.Approximately(multiplier, _appliedMultiplier)) return;

            _appliedMultiplier = multiplier;
            _health.DamageTakenMultiplier = multiplier;
        }
    }
}