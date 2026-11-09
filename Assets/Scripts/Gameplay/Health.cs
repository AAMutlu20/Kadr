using System;
using Core;
using Events;
using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// Hit points for any entity: the player, regular enemies, bosses. This is the model side of the
    /// health feature, so it knows nothing about the HUD, audio or XP.
    ///
    /// Two ways to hear about it, for two different audiences:
    /// - HealthChangedEvent goes on the event bus for systems elsewhere in the game (the health GUI, the
    ///   enemy health bars, damage vignettes).
    /// - The Died C# event is for components on this same GameObject (PlayerController stops reading input,
    ///   an enemy controller starts its death state and publishes EnemyKilled). Keeping it local avoids
    ///   a global "someone died" event that every listener would have to filter by entity.
    ///
    /// HealthChangedEvent is also published once in Start, so a HUD that subscribed early shows the full
    /// bar immediately instead of staying empty until the first hit.
    /// </summary>
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] private float maxHealth = 100f;

        private float _current;

        public float Current => _current;
        public float Max => maxHealth;
        public bool IsDead { get; private set; }

        /// <summary>Raised once, after the final HealthChangedEvent, when health reaches zero.</summary>
        public event Action Died;

        private void Awake()
        {
            _current = maxHealth;
        }

        private void Start()
        {
            PublishChange(0f);
        }

        public void TakeDamage(float amount, GameObject source)
        {
            if (IsDead || amount <= 0f)
            {
                return;
            }

            SetCurrent(_current - amount);

            if (!(_current <= 0f)) return;
            IsDead = true;
            Died?.Invoke();
        }

        /// <summary>Restores health, capped at the maximum. A dead entity cannot be healed.</summary>
        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return;
            }

            SetCurrent(_current + amount);
        }

        /// <summary>Back to full health and alive. Used when a run restarts and the player object is reused.</summary>
        public void ResetToFull()
        {
            IsDead = false;
            SetCurrent(maxHealth);
        }

        private void SetCurrent(float value)
        {
            var clamped = Mathf.Clamp(value, 0f, maxHealth);
            var delta = clamped - _current;

            _current = clamped;
            PublishChange(delta);
        }

        private void PublishChange(float delta)
        {
            // TryGet, not Get: in edit mode (or during shutdown) there is no bus, and that is not an error.
            if (GameServices.TryGet(out EventBus bus))
            {
                bus.Publish(new HealthChangedEvent(gameObject, _current, maxHealth, delta));
            }
        }

        // Test helpers: right-click the component header in Play mode.
        [ContextMenu("Debug: Take 10 Damage")]
        private void DebugDamage()
        {
            TakeDamage(10f, null);
        }

        [ContextMenu("Debug: Heal 10")]
        private void DebugHeal()
        {
            Heal(10f);
        }
    }
}