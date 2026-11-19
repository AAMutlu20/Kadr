using Gameplay;
using UnityEngine;

namespace Items
{
    /// <summary>
    /// Heals the user: some at once, some over time. Any mix, so one effect type covers all the healing items:
    ///   Bandage   instant 0,  over time 25
    ///   Wipes     instant 5, over time 0
    ///   Medkit    instant 50, over time 25   (75 in total)
    /// Healing over time from several items adds up: each item keeps its own pool and they run side by side.
    /// The item is not used up when it would do nothing: dead, or already at full health.
    /// </summary>
    [CreateAssetMenu(menuName = "Kadr/Effects/Heal", fileName = "HealEffect")]
    public class HealEffect : ItemEffect
    {
        [Tooltip("Health restored the moment the item is used.")]
        [SerializeField, Min(0f)] private float amount = 50f;
        [Tooltip("Health restored gradually after that, spread over Over Time Seconds.")]
        [SerializeField, Min(0f)] private float overTimeAmount = 25f;
        [SerializeField, Min(0.1f)] private float overTimeSeconds = 10f;

        public override bool Use(GameObject user)
        {
            var health = user.GetComponent<Health>();
            if (!health || health.IsDead || health.Current >= health.Max) return false;

            if (amount > 0f) health.Heal(amount);
            if (overTimeAmount > 0f) HealOverTime.Apply(user, overTimeAmount, overTimeSeconds);
            return true;
        }
    }
}