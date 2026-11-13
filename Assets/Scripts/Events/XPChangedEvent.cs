namespace Events
{
    /// <summary>
    /// Published whenever the player's XP changes. The inventory screen shows Total on its top right, and the
    /// future XP GUI will listen to the same event. Delta lets a "+25 XP" popup work without remembering the old value.
    /// </summary>
    public readonly struct XpChangedEvent : IGameEvent
    {
        public readonly int Total;
        public readonly int Delta;

        public XpChangedEvent(int total, int delta)
        {
            Total = total;
            Delta = delta;
        }
    }
}