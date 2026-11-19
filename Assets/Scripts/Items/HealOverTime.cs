using System.Collections.Generic;
using Gameplay;
using UnityEngine;

namespace Items
{
    /// <summary>
    /// Gradual healing. Added to the user automatically the first time something heals over time, so nothing needs
    /// setting up. Every use adds a pool of health that drains evenly over its time; pools run side by side.
    /// Runs on game time, so it pauses with the inventory. Stops when the user dies.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class HealOverTime : MonoBehaviour
    {
        private sealed class Pool
        {
            public float PerSecond;
            public float Remaining;
        }

        private readonly List<Pool> _pools = new();
        private Health _health;

        /// <summary>True while any healing is still to come. The HUD can show a small "+" from it later.</summary>
        public bool IsHealing => _pools.Count > 0;

        public static void Apply(GameObject user, float amount, float seconds)
        {
            if (amount <= 0f || seconds <= 0f) return;

            if (!user.TryGetComponent<HealOverTime>(out var healer)) healer = user.AddComponent<HealOverTime>();
            healer._pools.Add(new Pool { PerSecond = amount / seconds, Remaining = amount });
        }

        private void Awake()
        {
            _health = GetComponent<Health>();
        }

        private void Update()
        {
            if (_pools.Count == 0) return;

            if (_health.IsDead)
            {
                _pools.Clear();
                return;
            }

            for (var i = _pools.Count - 1; i >= 0; i--)
            {
                var pool = _pools[i];
                var give = Mathf.Min(pool.Remaining, pool.PerSecond * Time.deltaTime);
                pool.Remaining -= give;
                _health.Heal(give);

                if (pool.Remaining <= 0.001f) _pools.RemoveAt(i);
            }
        }
    }
}