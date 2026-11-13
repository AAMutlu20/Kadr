using UnityEngine;

namespace Items
{
    /// <summary>
    /// An item lying in the world. Needs a Collider with Is Trigger ticked. Walking into it puts it in the
    /// inventory; if the grid is full it stays (and keeps trying while you stand in it, so freeing a slot works).
    /// A partial fit lowers the count left on the ground.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ItemPickup : MonoBehaviour
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField, Min(1)] private int count = 1;

        private void OnTriggerStay(Collider other)
        {
            var inventory = other.GetComponentInParent<Inventory>();
            if (!inventory || !item) return;

            count = inventory.TryAdd(item, count);
            if (count <= 0) Destroy(gameObject);
        }

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }
    }
}