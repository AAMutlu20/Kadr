using Generation;

namespace Events
{
    /// <summary>
    /// Published once a level has been fully built.
    /// </summary>
    public readonly struct LevelGeneratedEvent : IGameEvent
    {
        public readonly GeneratedLevel Level;

        public LevelGeneratedEvent(GeneratedLevel level)
        {
            Level = level;
        }
    }
}