namespace Events
{
    /// <summary>The photo camera was raised or lowered. The viewfinder overlay listens.</summary>
    public readonly struct CameraRaisedEvent : IGameEvent
    {
        public readonly bool Raised;
        public readonly float FrameFraction; // the square frame's height as a share of the screen height

        public CameraRaisedEvent(bool raised, float frameFraction)
        {
            Raised = raised;
            FrameFraction = frameFraction;
        }
    }
}