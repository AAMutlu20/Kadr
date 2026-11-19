using Enemies;
using UnityEngine;

namespace Photography
{
    /// <summary>
    /// Applies a flash ability to the enemies that were in the photo. Stun, Disorient and Fireshow act on enemies
    /// (through an EnemyStatus that is added when needed). Reveal acts on the player's own view and is handled by the
    /// camera controller.
    /// </summary>
    public static class FlashAbilities
    {
        /// <summary>Returns how many enemies were affected.</summary>
        public static int Apply(CameraAbility ability, AbilityData data, AbilityLevel level, PhotoData photo, GameObject source,
            GameObject burnEffect = null, float burnEffectScale = 1f)
        {
            var count = 0;

            foreach (var subject in photo.Subjects)
            {
                var enemy = subject.Source ? subject.Source.Enemy : null;
                if (!enemy || enemy.IsDead) continue;

                if (!enemy.TryGetComponent<EnemyStatus>(out var status)) status = enemy.gameObject.AddComponent<EnemyStatus>();

                switch (ability)
                {
                    case CameraAbility.Stun: status.Stun(level.duration); break;
                    case CameraAbility.Disorient: status.Disorient(level.duration); break;
                    case CameraAbility.Fireshow: status.Burn(level.duration, data.damagePerSecond, source, burnEffect, burnEffectScale); break;
                    default: return 0;
                }

                count++;
            }

            return count;
        }
    }
}