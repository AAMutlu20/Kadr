namespace Events
{
    /// <summary>
    /// Boss music phases. The names match the labels of the FMOD "BossPhase" parameter,
    /// so the audio coordinator can pass them straight through.
    /// </summary>
    public enum BossPhase
    {
        Patrol,
        Aggro,
        Enrage,
        Defeat
    }

    /// <summary>
    /// Published by a boss's state machine whenever its phase changes.
    /// The combat audio coordinator subscribes to it, so the boss never knows audio exists.
    /// </summary>
    public readonly struct BossPhaseChangedEvent : IGameEvent
    {
        public readonly BossPhase Phase;

        public BossPhaseChangedEvent(BossPhase phase)
        {
            Phase = phase;
        }
    }
}