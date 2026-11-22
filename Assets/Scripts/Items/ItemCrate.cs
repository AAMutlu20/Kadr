using System;
using System.Collections.Generic;
using Core;
using Events;
using UnityEngine;

namespace Items
{
    /// <summary>
    /// A container with its own inventory grid: a heal box, an ammo box, a locker, a drawer. Look at it and press E
    /// and a window opens with the crate's items next to the suitcase. Click an item to move it across, in either
    /// direction (storing can be switched off for things like a heal box).
    ///
    ///   Grid Width/Height   how much the crate can hold, in cells (items have footprints, like the suitcase)
    ///   Contents            what is in it at the start
    ///   Allow Storing       the player may put things in (lockers yes, a heal box no)
    ///   Destroy When Empty  the crate disappears once it has been emptied (heal boxes)
    ///   Required Key        optional: a key item the player must carry. Consume Key uses it up on opening.
    ///
    /// Needs a Collider (the PickupInteractor's ray must hit it). The window itself is CrateOverlay, which creates
    /// itself the first time a crate is opened.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ItemCrate : MonoBehaviour, IPickupTarget
    {
        [Serializable]
        public class Entry
        {
            public ItemDefinition item;
            [Min(1)] public int count = 1;
        }

        [Serializable]
        public class LootEntry
        {
            public ItemDefinition item;
            [Tooltip("Fewest of this item when it shows up. 1 = it always has at least one when it appears.")]
            [Min(1)] public int minCount = 1;
            [Tooltip("Most of this item the crate can hold.")]
            [Min(1)] public int maxCount = 1;
            [Tooltip("Chance that this item is in the crate at all. 1 = always, 0.25 = one crate in four.")]
            [Range(0f, 1f)] public float chance = 0.5f;
        }

        [SerializeField] private string displayName = "Crate";

        [Header("Space (cells)")]
        [SerializeField, Min(1)] private int gridWidth = 4;
        [SerializeField, Min(1)] private int gridHeight = 3;

        [Header("Contents at the start")]
        [SerializeField] private Entry[] contents = Array.Empty<Entry>();

        [Header("Random loot (rolled once when the crate is created)")]
        [Tooltip("Each entry is rolled on its own: it shows up with its Chance, and then in a random amount from Min to Max. " +
                 "Every crate rolls differently. This comes on top of the fixed Contents above.")]
        [SerializeField] private LootEntry[] lootTable = Array.Empty<LootEntry>();
        [Tooltip("Most different kinds of random item in one crate. 0 = no limit.")]
        [SerializeField, Min(0)] private int maxDifferentItems;
        [Tooltip("If the rolls come up empty, put one random item from the table in anyway.")]
        [SerializeField] private bool neverEmpty = true;

        [Header("Rules")]
        [Tooltip("The player can put things in. Off for a heal box.")]
        [SerializeField] private bool allowStoring = true;
        [Tooltip("The crate disappears when it has been emptied.")]
        [SerializeField] private bool destroyWhenEmpty;

        [Header("Lock (optional)")]
        [Tooltip("Key item the player must have in the suitcase to open it. Empty = never locked.")]
        [SerializeField] private ItemDefinition requiredKey;
        [Tooltip("The key is used up when the crate is unlocked. Off = the key stays and the crate stays open.")]
        [SerializeField] private bool consumeKey;

        private bool _unlocked;

        /// <summary>The window opened or closed. CrateLid listens to these to move the lid.</summary>
        public event Action Opened;
        public event Action Closed;

        public string DisplayName => displayName;
        public bool AllowStoring => allowStoring;

        /// <summary>What is in the crate. A plain grid, same as the suitcase's.</summary>
        public InventoryGrid Grid { get; private set; }

        public string Prompt => requiredKey && !_unlocked ? displayName + "  (locked)" : "Open " + displayName;

        private void Awake()
        {
            Grid = new InventoryGrid(gridWidth, gridHeight);

            foreach (var entry in contents)
            {
                if (entry == null || !entry.item || entry.count <= 0) continue;

                var leftover = Grid.Add(entry.item, entry.count);
                if (leftover > 0)
                {
                    Debug.LogWarning($"[ItemCrate] {displayName}: {leftover} x {entry.item.DisplayName} did not fit. " +
                                     "Make the grid bigger or put less in it.", this);
                }
            }

            RollLoot();
        }

        /// <summary>
        /// Rolls the random loot table. The entries are visited in random order (so the "most different items" limit
        /// does not always favour the top of the list), then the biggest items are placed first so a full crate
        /// loses small things instead of the one big thing.
        /// </summary>
        private void RollLoot()
        {
            if (lootTable == null || lootTable.Length == 0) return;

            var order = new List<int>();
            for (var i = 0; i < lootTable.Length; i++) order.Add(i);
            for (var i = order.Count - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var rolled = new List<(ItemDefinition item, int count)>();
            foreach (var index in order)
            {
                if (maxDifferentItems > 0 && rolled.Count >= maxDifferentItems) break;

                var entry = lootTable[index];
                if (entry == null || !entry.item) continue;
                if (UnityEngine.Random.value > entry.chance) continue;

                var max = Mathf.Max(entry.minCount, entry.maxCount);
                rolled.Add((entry.item, UnityEngine.Random.Range(Mathf.Max(1, entry.minCount), max + 1)));
            }

            if (rolled.Count == 0 && neverEmpty && Grid.Stacks.Count == 0)
            {
                var pool = new List<LootEntry>();
                foreach (var entry in lootTable)
                {
                    if (entry != null && entry.item) pool.Add(entry);
                }

                if (pool.Count > 0)
                {
                    var pick = pool[UnityEngine.Random.Range(0, pool.Count)];
                    rolled.Add((pick.item, UnityEngine.Random.Range(1, Mathf.Max(1, pick.maxCount) + 1)));
                }
            }

            rolled.Sort((a, b) => (b.item.Width * b.item.Height).CompareTo(a.item.Width * a.item.Height));

            foreach (var (item, count) in rolled)
            {
                var leftover = Grid.Add(item, count);
                if (leftover > 0)
                {
                    Debug.LogWarning($"[ItemCrate] {displayName}: {leftover} x {item.DisplayName} of the random loot did not fit " +
                                     "and was dropped. Make the grid bigger.", this);
                }
            }
        }

        /// <summary>E on the crate. Opens the window, or says it is locked. Returns true so no "Inventory full" shows.</summary>
        public bool TryCollect(Inventory inventory)
        {
            if (!inventory) return false;

            if (Unlock(inventory)) CrateOverlay.Show(this, inventory);
            return true;
        }

        /// <summary>Called by the window when it opens.</summary>
        public void OnOpened() => Opened?.Invoke();

        /// <summary>Called by the window when it closes.</summary>
        public void OnClosed()
        {
            Closed?.Invoke();

            if (destroyWhenEmpty && Grid.Stacks.Count == 0) Destroy(gameObject);
        }

        private bool Unlock(Inventory inventory)
        {
            if (!requiredKey || _unlocked) return true;

            var grid = inventory.GridFor(requiredKey.Category);
            var key = grid.Find(requiredKey);
            if (key == null)
            {
                Notice("Locked. Needs: " + requiredKey.DisplayName);
                return false;
            }

            _unlocked = true;
            if (consumeKey) grid.RemoveCount(key, 1);

            Notice("Unlocked");
            return true;
        }

        private static void Notice(string message)
        {
            if (GameServices.TryGet(out EventBus bus)) bus.Publish(new NoticeEvent(message));
        }
    }
}