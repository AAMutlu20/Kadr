using UnityEngine;

namespace Items
{
    /// <summary>
    /// An item lying in the world. Needs a Collider (the PickupInteractor's ray must hit it).
    ///
    /// Default: look at it and press E (the PickupInteractor does that, and shows the prompt).
    /// Auto Pickup: walk into it instead. That needs Is Trigger ticked, and suits things like ammo or XP.
    ///
    /// If the grid is full the pickup stays; a partial fit lowers the count left on the ground.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ItemPickup : MonoBehaviour
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField, Min(1)] private int count = 1;
        [Tooltip("Collect by walking into it (needs Is Trigger on the collider). Off = look at it and press E.")]
        [SerializeField] private bool autoPickup;

        /// <summary>What the prompt says while the player looks at it.</summary>
        public string Prompt => !item ? "" : count > 1 ? $"{item.DisplayName}  x{count}" : item.DisplayName;

        /// <summary>Tries to put it in the inventory. True if at least one item went in.</summary>
        public bool TryCollect(Inventory inventory)
        {
            if (!item || !inventory) return false;

            var leftover = inventory.TryAdd(item, count);
            var taken = count - leftover;
            count = leftover;

            if (count <= 0) Destroy(gameObject);
            return taken > 0;
        }

        private void OnTriggerStay(Collider other)
        {
            if (!autoPickup) return;

            var inventory = other.GetComponentInParent<Inventory>();
            if (inventory) TryCollect(inventory);
        }
    }
}