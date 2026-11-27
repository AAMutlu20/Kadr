namespace Events
{
    /// <summary>
    /// The player just died. Published once, before the debrief opens (the screen is already black by then).
    /// DeathNumber is 1 for the first death of this play session. The Comisar's first-death scene listens to this.
    /// </summary>
    public readonly struct PlayerDiedEvent : IGameEvent
    {
        public readonly int DeathNumber;

        public PlayerDiedEvent(int deathNumber)
        {
            DeathNumber = deathNumber;
        }
    }

    /// <summary>
    /// A new run has started after a death: the level is regenerated, the player is back in the lobby with full health
    /// and the penalties applied. Scene systems that keep run state (quests, the cage, the boss arena) reset on this.
    /// It comes while the screen is still black.
    /// </summary>
    public readonly struct RunRestartedEvent : IGameEvent
    {
        public readonly int DeathNumber;

        public RunRestartedEvent(int deathNumber)
        {
            DeathNumber = deathNumber;
        }
    }
}