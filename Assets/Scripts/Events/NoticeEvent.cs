namespace Events
{
    /// <summary>A short message for the player ("Inventory full"). The prompt view shows it for a moment.</summary>
    public readonly struct NoticeEvent : IGameEvent
    {
        public readonly string Message;

        public NoticeEvent(string message)
        {
            Message = message;
        }
    }
}