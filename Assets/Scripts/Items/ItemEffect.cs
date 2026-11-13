using UnityEngine;

namespace Items
{
    /// <summary>
    /// What happens when an item is used. A ScriptableObject so each effect is an asset (a potion that heals
    /// 50, a potion that heals 100) and so a new kind of item is a new subclass, not a change to Inventory.
    /// Return true only if the effect actually did something, so the item is consumed only then.
    /// </summary>
    public abstract class ItemEffect : ScriptableObject
    {
        public abstract bool Use(GameObject user);
    }
}