namespace Items
{
    /// <summary>
    /// One item on the grid: what it is, how many, where its top-left cell is, and whether it lies rotated.
    /// Plain class (not a struct) because the UI and the grid both hold references to the same stack.
    /// </summary>
    public class ItemStack
    {
        public ItemDefinition Definition;
        public int Count;
        public int X;
        public int Y;
        public bool Rotated;

        /// <summary>Footprint in cells, already taking rotation into account.</summary>
        public int Width => Rotated ? Definition.Height : Definition.Width;
        public int Height => Rotated ? Definition.Width : Definition.Height;
    }
}