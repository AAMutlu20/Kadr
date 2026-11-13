namespace Events
{
    /// <summary>
    /// Published when items actually enter the inventory (not when they were refused for lack of space).
    /// Quests ("find the documents"), the HUD pickup toast and audio listen to this.
    /// </summary>
    public readonly struct ItemCollectedEvent : IGameEvent
    {
        public readonly string ItemId;
        public readonly string DisplayName;
        public readonly int Count;

        public ItemCollectedEvent(string itemId, string displayName, int count)
        {
            ItemId = itemId;
            DisplayName = displayName;
            Count = count;
        }
    }
}