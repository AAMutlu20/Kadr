using UnityEngine;
using UnityEngine.UI;

namespace Gameplay
{
    /// <summary>
    /// Sprint stamina. Put it on the Player next to PlayerController. It builds its own thin bar at the bottom centre
    /// of the screen, so nothing else needs setting up.
    ///
    ///   Sprinting drains it. Walking, standing and being still refill it after a short delay.
    ///   At zero the player is exhausted: no sprinting until it has recovered to the Recover Threshold
    ///   (so it can't flicker on and off at the edge). The bar turns red while exhausted.
    ///   The bar fades out while full and only shows up when there is something to see.
    ///
    /// WeaponController asks CanSprint, so aiming, reloading and being out of breath all use the same switch
    /// on PlayerController. Leninade, wine and upgrades call Restore() or SetMax().
    /// Time.deltaTime is 0 while the suitcase is open, so nothing drains or refills then.
    /// PlayerBuffs changes it: Leninade stops the drain (the bar turns blue and counts the effect down), being drunk
    /// drains faster and refills slower.
    /// </summary>
    public class PlayerStamina : MonoBehaviour
    {
        [Header("References (empty = found automatically)")]
        [SerializeField] private PlayerController player;

        [Header("Stamina")]
        [SerializeField, Min(1f)] private float maxStamina = 100f;
        [Tooltip("Per second while sprinting. 20 with a max of 100 = 5 seconds of sprint.")]
        [SerializeField, Min(0f)] private float drainPerSecond = 20f;
        [Tooltip("Per second while not sprinting, once the delay is over. 25 = 4 seconds from empty to full.")]
        [SerializeField, Min(0f)] private float regenPerSecond = 25f;
        [Tooltip("Seconds after the last sprint before it starts refilling.")]
        [SerializeField, Min(0f)] private float regenDelay = 1f;
        [Tooltip("After running dry, sprinting is locked until stamina is back to this fraction (0.25 = a quarter).")]
        [SerializeField, Range(0f, 1f)] private float recoverThreshold = 0.25f;

        private CharacterController _controller;
        private float _stamina;
        private float _sinceSprint;
        private bool _exhausted;
        private bool _sprinting;


        public float Fraction => _stamina / maxStamina;
        public bool IsExhausted => _exhausted;
        public bool IsSprinting => _sprinting;

        /// <summary>False while out of breath. WeaponController combines this with its own aim and reload rules.</summary>
        public bool CanSprint => !_exhausted;

        /// <summary>Adds stamina (Leninade, wine). Clears the exhaustion if it brings the player past the threshold.</summary>
        public void Restore(float amount)
        {
            _stamina = Mathf.Min(maxStamina, _stamina + amount);
            if (_exhausted && Fraction >= recoverThreshold) _exhausted = false;
        }

        /// <summary>For upgrades. Keeps the same fraction of the bar filled.</summary>
        public void SetMax(float newMax)
        {
            var fraction = Fraction;
            maxStamina = Mathf.Max(1f, newMax);
            _stamina = maxStamina * fraction;
        }

        private void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
            _controller = GetComponent<CharacterController>();
            _stamina = maxStamina;
            _sinceSprint = regenDelay;
        }

        private void Update()
        {
            var dt = Time.deltaTime;

            var buffs = PlayerBuffs.Instance;
            var infinite = buffs && buffs.InfiniteStamina; // Leninade
            var drain = drainPerSecond * (buffs ? buffs.StaminaDrainMultiplier : 1f); // drunk: faster
            var regen = regenPerSecond * (buffs ? buffs.StaminaRegenMultiplier : 1f); // drunk: slower

            if (infinite) _exhausted = false;
            _sprinting = !_exhausted && IsReallySprinting();

            if (_sprinting)
            {
                _sinceSprint = 0f;
                if (!infinite) _stamina = Mathf.Max(0f, _stamina - drain * dt);
                if (_stamina <= 0f) _exhausted = true;
            }
            else
            {
                _sinceSprint += dt;
                if (_sinceSprint >= regenDelay || infinite)
                {
                    _stamina = Mathf.Min(maxStamina, _stamina + regen * dt);
                    if (_exhausted && Fraction >= recoverThreshold) _exhausted = false;
                }
            }
        }

        /// <summary>Same condition PlayerController uses to sprint, plus actually moving (running into a wall costs nothing).</summary>
        private bool IsReallySprinting()
        {
            if (!player || !player.InputEnabled || !player.CanSprint) return false;
            if (!GameInput.SprintHeld() || GameInput.Move().y <= 0f) return false;
            if (!_controller) return true;

            var v = _controller.velocity;
            v.y = 0f;
            return v.sqrMagnitude > 1f;
        }
    }
}