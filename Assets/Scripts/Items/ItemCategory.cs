namespace Items
{
    /// <summary>
    /// Which compartment of the suitcase an item belongs to. Consumables live in the bottom half (the red grid),
    /// permanent items (weapons, camera, torch, key items) live in the lid (the green grid). An item can only be
    /// placed in the grid of its own category.
    /// </summary>
    public enum ItemCategory
    {
        Consumable,
        Permanent
    }
}