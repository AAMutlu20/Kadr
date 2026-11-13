using Core;
using Events;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Items
{
    /// <summary>
    /// Lives on the Player. Owns the grid and is the one door into it: pickups call TryAdd, the UI and the
    /// hotkey call TryUse. The grid itself stays a plain model with no events on the bus.
    ///
    /// The inventory is real time: opening it never pauses the game (the UI freezes the player's movement,
    /// not the world). It is run scoped, so a new run simply creates a fresh Inventory.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        [Header("Grid size (cells)")]
        [SerializeField, Min(1)] private int width = 8;
        [SerializeField, Min(1)] private int height = 6;

        [Header("Quick use")]
        [Tooltip("The item the H key uses without opening the inventory (the first-aid med).")]
        [SerializeField] private ItemDefinition quickHealItem;

        [Header("Testing")]
        [Tooltip("Added once at the start of play, in order. Put Pistol, Shotgun, Camera first: they land in the top left.")]
        [SerializeField] private ItemDefinition[] startingItems;

        public InventoryGrid Grid { get; private set; }

        /// <summary>Set by InventoryUI. While open the hotkey is ignored.</summary>
        public bool IsOpen { get; set; }

        private void Awake()
        {
            Grid = new InventoryGrid(width, height);
        }

        private void Start()
        {
            if (startingItems == null) return;

            foreach (var item in startingItems)
            {
                if (item) TryAdd(item);
            }
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || IsOpen || !quickHealItem) return;

            if (kb.hKey.wasPressedThisFrame)
            {
                var stack = Grid.Find(quickHealItem);
                if (stack != null) TryUse(stack);
            }
        }

        /// <summary>Returns how many items did not fit (0 = all stored).</summary>
        public int TryAdd(ItemDefinition def, int count = 1)
        {
            var leftover = Grid.Add(def, count);
            var added = count - leftover;

            if (added > 0 && GameServices.TryGet(out EventBus bus))
            {
                bus.Publish(new ItemCollectedEvent(def.Id, def.DisplayName, added));
            }

            return leftover;
        }

        /// <summary>Uses one item of the stack. False if it has no effect or the effect refused.</summary>
        public bool TryUse(ItemStack stack)
        {
            var effect = stack?.Definition.Effect;
            if (!effect || !effect.Use(gameObject)) return false;

            Grid.RemoveCount(stack, 1);
            return true;
        }
    }
}