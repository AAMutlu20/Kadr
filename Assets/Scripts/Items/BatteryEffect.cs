using UnityEngine;

namespace Items
{
    /// <summary>
    /// A battery: fully recharges the flashlight. Not used up when the player has no flashlight or it is already full.
    /// Create with: right click in Project, Create, Kadr, Effects, Battery. Or let Tools > Kadr > Create Item Catalog do it.
    /// </summary>
    [CreateAssetMenu(menuName = "Kadr/Effects/Battery", fileName = "BatteryEffect")]
    public class BatteryEffect : ItemEffect
    {
        public override bool Use(GameObject user)
        {
            var flashlight = user.GetComponent<Flashlight>();
            if (!flashlight || !flashlight.CanRecharge) return false;

            flashlight.Recharge();
            return true;
        }
    }
}