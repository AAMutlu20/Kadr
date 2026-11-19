namespace Events
{
    /// <summary>Night vision (the Reveal ability) switched on or off. The viewfinder lightens its outside dimming.</summary>
    public readonly struct NightVisionEvent : IGameEvent
    {
        public readonly bool Active;

        public NightVisionEvent(bool active)
        {
            Active = active;
        }
    }
}