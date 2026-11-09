using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// Anything a weapon or hazard can hurt. Weapons find the target with GetComponentInParent and call this,
    /// so they never depend on whether they hit the player, an enemy or a boss weak point.
    /// Health implements it; a separate hitbox (a boss's weak spot with a damage multiplier, for example) can too
    /// and forward the damage to a Health higher up the hierarchy.
    /// </summary>
    public interface IDamageable
    {
        /// <param name="amount">Damage dealt. Zero or negative amounts are ignored.</param>
        /// <param name="source">Whoever caused it, for knockback direction and kill credit. May be null.</param>
        void TakeDamage(float amount, GameObject source);
    }
}