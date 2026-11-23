namespace Events
{
    /// <summary>The boss fight began (the player entered the arena) or ended. Music and doors can listen.</summary>
    public readonly struct BossEncounterEvent : IGameEvent
    {
        public readonly bool Started;

        public BossEncounterEvent(bool started)
        {
            Started = started;
        }
    }

    /// <summary>The boss finished changing form. For Tree Head: Quadruped = the fast four legged form.</summary>
    public readonly struct BossFormEvent : IGameEvent
    {
        public readonly bool Quadruped;

        public BossFormEvent(bool quadruped)
        {
            Quadruped = quadruped;
        }
    }
}