using Gameplay;
using UnityEngine;

namespace Items
{
    /// <summary>
    /// Alcohol: takes damage down for a while, and makes the player drunk (camera wobble, a little less stamina, wider
    /// spread; see PlayerBuffs for how strong each is at full drunkenness). Drinking more refreshes the time and adds to
    /// how drunk the player is.
    /// Create with: right click in Project, Create, Kadr, Effects, Alcohol. Or let Tools > Kadr > Create Item Catalog do it.
    /// </summary>
    [CreateAssetMenu(menuName = "Kadr/Effects/Alcohol", fileName = "AlcoholEffect")]
    public class AlcoholEffect : ItemEffect
    {
        [Tooltip("How long the damage resistance lasts. The drunkenness lingers a little longer.")]
        [SerializeField, Min(1f)] private float seconds = 30f;
        [Tooltip("Damage taken is multiplied by this. 0.6 = 40% less damage.")]
        [SerializeField, Range(0.1f, 1f)] private float damageTakenMultiplier = 0.6f;
        [Tooltip("How drunk one drink makes the player, 0 to 1. Two drinks at 0.5 = as drunk as it gets.")]
        [SerializeField, Range(0.05f, 1f)] private float drunkStrength = 0.5f;

        public override bool Use(GameObject user)
        {
            var health = user.GetComponent<Health>();
            if (health && health.IsDead) return false;

            PlayerBuffs.Of(user).Drink(seconds, damageTakenMultiplier, drunkStrength);
            return true;
        }
    }
}