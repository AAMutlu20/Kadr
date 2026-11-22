namespace Items
{
    /// <summary>Anything the PickupInteractor can look at and collect with E: a single item or a crate of several.</summary>
    public interface IPickupTarget
    {
        /// <summary>What the prompt says while the player looks at it.</summary>
        string Prompt { get; }

        /// <summary>True if at least one item went in the inventory.</summary>
        bool TryCollect(Inventory inventory);
    }
}