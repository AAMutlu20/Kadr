using System.Collections.Generic;
using Core;
using Events;
using UnityEngine;
using UnityEngine.InputSystem;
using Weapons;

namespace Items
{
    public enum AmmoKind
    {
        Normal,
        Incendiary
    }

    /// <summary>
    /// The pistol's spare ammo, which lives in the suitcase as ordinary items. On the Player next to the Inventory.
    /// The weapon asks it for rounds when it reloads: Count says how many are left, Take removes them from the
    /// suitcase. It also remembers which kind the player has selected (toggle key X).
    ///
    /// The player starts a run with only the full magazine in the pistol, so the reserve starts empty.
    /// </summary>
    public class AmmoReserve : MonoBehaviour
    {
        [Header("References (empty = found automatically)")]
        [SerializeField] private Inventory inventory;

        [Header("Items")]
        [SerializeField] private ItemDefinition normalAmmo;
        [SerializeField] private ItemDefinition incendiaryAmmo;

        [Header("Keys")]
        [Tooltip("Switches between normal and incendiary rounds for the next reload.")]
        [SerializeField] private Key toggleKey = Key.X;

        private EventBus _bus;

        /// <summary>The first AmmoReserve in the scene. The weapons use this, so they need no reference.</summary>
        public static AmmoReserve Instance { get; private set; }

        /// <summary>The kind the next reload loads.</summary>
        public AmmoKind Selected { get; private set; } = AmmoKind.Normal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (!inventory) inventory = GetComponentInParent<Inventory>();
            if (!inventory) inventory = GetComponent<Inventory>();
            Instance = this;

            PistolAmmoPanel.Create(transform, this, inventory);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || Time.timeScale == 0f || (inventory && inventory.IsOpen)) return;

            if (kb[toggleKey].wasPressedThisFrame) ToggleSelected();
        }

        public ItemDefinition Item(AmmoKind kind) => kind == AmmoKind.Incendiary ? incendiaryAmmo : normalAmmo;

        public string Label(AmmoKind kind) => kind == AmmoKind.Incendiary ? "incendiary" : "normal";

        /// <summary>How many rounds of this kind are in the suitcase.</summary>
        public int Count(AmmoKind kind)
        {
            var item = Item(kind);
            if (!item || !inventory) return 0;

            var total = 0;
            foreach (var stack in inventory.GridFor(item.Category).Stacks)
            {
                if (stack.Definition == item) total += stack.Count;
            }

            return total;
        }

        /// <summary>Takes up to 'max' rounds out of the suitcase and returns how many it got.</summary>
        public int Take(AmmoKind kind, int max)
        {
            var item = Item(kind);
            if (!item || !inventory || max <= 0) return 0;

            var grid = inventory.GridFor(item.Category);
            var stacks = new List<ItemStack>();
            foreach (var stack in grid.Stacks)
            {
                if (stack.Definition == item) stacks.Add(stack);
            }

            var taken = 0;
            foreach (var stack in stacks)
            {
                if (taken >= max) break;

                var amount = Mathf.Min(stack.Count, max - taken);
                grid.RemoveCount(stack, amount);
                taken += amount;
            }

            return taken;
        }

        /// <summary>Puts rounds back into the suitcase (an unloaded magazine). Returns how many did not fit.</summary>
        public int Give(AmmoKind kind, int count)
        {
            var item = Item(kind);
            if (!item || !inventory || count <= 0) return count;

            return inventory.TryAdd(item, count);
        }

        public void Select(AmmoKind kind)
        {
            if (Selected == kind) return;

            Selected = kind;
            Notice("Ammo: " + Label(kind) + "  (" + Count(kind) + " in the suitcase)");
        }

        private void ToggleSelected()
        {
            var other = Selected == AmmoKind.Normal ? AmmoKind.Incendiary : AmmoKind.Normal;

            if (Count(other) <= 0)
            {
                Notice("No " + Label(other) + " ammo in the suitcase");
                return;
            }

            Select(other);
        }

        private void Notice(string message)
        {
            if (_bus != null || GameServices.TryGet(out _bus)) _bus.Publish(new NoticeEvent(message));
        }
    }
}