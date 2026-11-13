using System;
using System.Collections.Generic;

namespace Items
{
    /// <summary>
    /// The model of a Resident Evil style inventory: a fixed grid where every item occupies a rectangle of
    /// cells and can be rotated. No Unity types, so it knows nothing about UI or input.
    ///
    /// A cell lookup array (_cells) points at the stack covering that cell, which makes "what is under the
    /// cursor" and "is this spot free" O(1) per cell. Every method that changes anything raises Changed once.
    /// </summary>
    public class InventoryGrid
    {
        private readonly ItemStack[,] _cells;
        private readonly List<ItemStack> _stacks = new();

        public InventoryGrid(int width, int height)
        {
            Width = width;
            Height = height;
            _cells = new ItemStack[width, height];
        }

        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<ItemStack> Stacks => _stacks;

        /// <summary>Raised after any change, so a view can redraw itself.</summary>
        public event Action Changed;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public ItemStack GetAt(int x, int y) => InBounds(x, y) ? _cells[x, y] : null;

        /// <summary>
        /// Whether a (possibly rotated) item fits with its top-left cell at x, y. 'ignore' is the stack being
        /// moved, so an item never blocks its own new position.
        /// </summary>
        public bool CanPlace(ItemDefinition def, int x, int y, bool rotated, ItemStack ignore = null)
        {
            var w = rotated ? def.Height : def.Width;
            var h = rotated ? def.Width : def.Height;

            if (x < 0 || y < 0 || x + w > Width || y + h > Height) return false;

            for (var cx = x; cx < x + w; cx++)
            {
                for (var cy = y; cy < y + h; cy++)
                {
                    var other = _cells[cx, cy];
                    if (other != null && other != ignore) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Adds items: tops up existing stacks first, then finds free space (trying the item rotated if it
        /// does not fit upright). Returns how many did NOT fit, so 0 means everything was stored.
        /// </summary>
        public int Add(ItemDefinition def, int count)
        {
            foreach (var stack in _stacks)
            {
                if (count <= 0) break;
                if (stack.Definition != def || stack.Count >= def.MaxStack) continue;

                var moved = Math.Min(def.MaxStack - stack.Count, count);
                stack.Count += moved;
                count -= moved;
            }

            while (count > 0 && TryFindSpace(def, out var x, out var y, out var rotated))
            {
                var amount = Math.Min(def.MaxStack, count);
                var stack = new ItemStack { Definition = def, Count = amount, X = x, Y = y, Rotated = rotated };

                _stacks.Add(stack);
                Fill(stack);
                count -= amount;
            }

            Changed?.Invoke();
            return count;
        }

        /// <summary>Moves a stack that is already on the grid (and optionally rotates it). False if it does not fit.</summary>
        public bool TryMove(ItemStack stack, int x, int y, bool rotated)
        {
            if (!CanPlace(stack.Definition, x, y, rotated, stack)) return false;

            Clear(stack);
            stack.X = x;
            stack.Y = y;
            stack.Rotated = rotated;
            Fill(stack);

            Changed?.Invoke();
            return true;
        }

        /// <summary>Pours 'from' into 'into' when both are the same stackable item. 'from' vanishes when emptied.</summary>
        public bool TryMerge(ItemStack from, ItemStack into)
        {
            if (from == into || from.Definition != into.Definition) return false;

            var room = into.Definition.MaxStack - into.Count;
            if (room <= 0) return false;

            var moved = Math.Min(room, from.Count);
            into.Count += moved;
            from.Count -= moved;

            if (from.Count <= 0) RemoveInternal(from);

            Changed?.Invoke();
            return true;
        }

        /// <summary>Takes items out of a stack, removing the stack when it reaches zero.</summary>
        public void RemoveCount(ItemStack stack, int amount)
        {
            stack.Count -= amount;
            if (stack.Count <= 0) RemoveInternal(stack);

            Changed?.Invoke();
        }

        /// <summary>Finds the first stack of an item, or null. Used by quick-use hotkeys.</summary>
        public ItemStack Find(ItemDefinition def)
        {
            foreach (var stack in _stacks)
            {
                if (stack.Definition == def) return stack;
            }

            return null;
        }

        private bool TryFindSpace(ItemDefinition def, out int x, out int y, out bool rotated)
        {
            // Upright first, then rotated. A square item has no point in being tried twice.
            for (var pass = 0; pass < 2; pass++)
            {
                rotated = pass == 1;
                if (rotated && def.Width == def.Height) break;

                for (y = 0; y < Height; y++)
                {
                    for (x = 0; x < Width; x++)
                    {
                        if (CanPlace(def, x, y, rotated)) return true;
                    }
                }
            }

            x = y = 0;
            rotated = false;
            return false;
        }

        private void RemoveInternal(ItemStack stack)
        {
            Clear(stack);
            _stacks.Remove(stack);
        }

        private void Fill(ItemStack stack) => Paint(stack, stack);

        private void Clear(ItemStack stack) => Paint(stack, null);

        private void Paint(ItemStack stack, ItemStack value)
        {
            for (var cx = stack.X; cx < stack.X + stack.Width; cx++)
            {
                for (var cy = stack.Y; cy < stack.Y + stack.Height; cy++)
                {
                    _cells[cx, cy] = value;
                }
            }
        }
    }
}