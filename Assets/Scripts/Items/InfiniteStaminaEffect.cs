using Core;
using Events;
using Gameplay;
using UnityEngine;

namespace Items
{
    /// <summary>
    /// Leninade: unlimited sprint stamina for a while. Not used up while one is still working.
    /// Create with: right click in Project, Create, Kadr, Effects, Infinite Stamina. Or let Tools > Kadr > Create Item Catalog do it.
    /// </summary>
    [CreateAssetMenu(menuName = "Kadr/Effects/Infinite Stamina", fileName = "InfiniteStaminaEffect")]
    public class InfiniteStaminaEffect : ItemEffect
    {
        [SerializeField, Min(1f)] private float seconds = 8f;

        public override bool Use(GameObject user)
        {
            var buffs = PlayerBuffs.Of(user);
            if (buffs.InfiniteStamina)
            {
                if (GameServices.TryGet(out EventBus bus)) bus.Publish(new NoticeEvent("Already full of energy"));
                return false;
            }

            buffs.GiveInfiniteStamina(seconds);
            return true;
        }
    }
}