using System.Collections.Generic;
using Core;
using Events;
using Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Items
{
    /// <summary>
    /// Lives on the Player. Owns the two grids and is the one door into them: pickups call TryAdd, the screen and
    /// the hotkey call TryUse. The grids themselves stay plain models with no events on the bus.
    ///
    /// Two compartments, like the suitcase: the Consumables grid (bottom half) and the Permanent grid (the lid, for
    /// weapons, camera, torch and key items). An item goes into the grid of its own category.
    ///
    /// Opening the inventory pauses the game (the screen sets Time.timeScale to 0). It is run scoped, so a new run
    /// simply creates a fresh Inventory.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        [Header("Consumables (bottom half of the case), in cells")]
        [SerializeField, Min(1)] private int consumableWidth = 8;
        [SerializeField, Min(1)] private int consumableHeight = 4;

        [Header("Permanent items (the lid), in cells")]
        [SerializeField, Min(1)] private int permanentWidth = 5;
        [SerializeField, Min(1)] private int permanentHeight = 8;

        [Header("Quick use")]
        [Tooltip("The item the H key uses without opening the inventory (the first-aid med).")]
        [SerializeField] private ItemDefinition quickHealItem;

        [Header("Testing")]
        [Tooltip("Added once at the start of play, in order, each into the grid of its category. " +
                 "Pistol, Shotgun, Camera first: they pack neatly into the lid.")]
        [SerializeField] private ItemDefinition[] startingItems;

        public InventoryGrid Consumables { get; private set; }
        public InventoryGrid Permanent { get; private set; }

        /// <summary>Both grids, for code that does not care which is which.</summary>
        public IReadOnlyList<InventoryGrid> Grids { get; private set; }

        /// <summary>Set by the inventory screen. While open the hotkey is ignored.</summary>
        public bool IsOpen { get; set; }

        private void Awake()
        {
            Consumables = new InventoryGrid(consumableWidth, consumableHeight);
            Permanent = new InventoryGrid(permanentWidth, permanentHeight);
            Grids = new[] { Consumables, Permanent };
        }

        private void Start()
        {
            // A loaded save brings its own inventory: the testing items would be added on top of it.
            if (startingItems == null || Saving.SaveSystem.LoadedFromSave) return;

            foreach (var item in startingItems)
            {
                if (item) TryAdd(item);
            }
        }

        private void Update()
        {
            if (IsOpen || !quickHealItem) return;

            if (KadrControls.Pressed(GameAction.QuickHeal))
            {
                var stack = GridFor(quickHealItem.Category).Find(quickHealItem);
                if (stack != null) TryUse(stack);
            }
        }

        public InventoryGrid GridFor(ItemCategory category)
        {
            return category == ItemCategory.Permanent ? Permanent : Consumables;
        }

        /// <summary>Returns how many items did not fit (0 = all stored).</summary>
        public int TryAdd(ItemDefinition def, int count = 1)
        {
            var leftover = GridFor(def.Category).Add(def, count);
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
            if (!effect) return false;

            var grid = FindGridOf(stack);
            if (grid == null || !effect.Use(gameObject)) return false;

            grid.RemoveCount(stack, 1);
            return true;
        }

        private InventoryGrid FindGridOf(ItemStack stack)
        {
            foreach (var grid in Grids)
            {
                foreach (var s in grid.Stacks)
                {
                    if (s == stack) return grid;
                }
            }

            return null;
        }
    }
}