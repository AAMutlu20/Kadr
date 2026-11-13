using Gameplay;
using UnityEngine;

namespace Items
{
    [CreateAssetMenu(menuName = "Kadr/Effects/Heal", fileName = "HealEffect")]
    public class HealEffect : ItemEffect
    {
        [SerializeField, Min(1f)] private float amount = 50f;

        public override bool Use(GameObject user)
        {
            var health = user.GetComponent<Health>();

            // Not consumed when it would do nothing: dead, or already at full health.
            if (!health || health.IsDead || health.Current >= health.Max) return false;

            health.Heal(amount);
            return true;
        }
    }
}