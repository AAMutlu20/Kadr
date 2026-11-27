namespace Events
{
    /// <summary>The player used the cage terminal while threats were still on the floor. The quest turns this into "clear the floor".</summary>
    public readonly struct CageBlockedEvent : IGameEvent
    {
        public readonly int Threats;

        public CageBlockedEvent(int threats)
        {
            Threats = threats;
        }
    }

    /// <summary>The cage was opened from the terminal: the subject is loose and the fight starts.</summary>
    public readonly struct CageOpenedEvent : IGameEvent
    {
    }
}