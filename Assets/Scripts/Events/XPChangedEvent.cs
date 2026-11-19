namespace Events
{
    /// <summary>
    /// Published whenever the player's XP or camera points change. XP fills a bar; every time the bar is full it gives
    /// one point, and the bar starts again with a bigger target. The suitcase bar listens to this.
    /// Delta is the XP just added (negative when a point was spent), PointEarned is true when the bar filled up.
    /// </summary>
    public readonly struct XpChangedEvent : IGameEvent
    {
        public readonly int Points;       // unspent camera points
        public readonly int Progress;     // XP in the current bar
        public readonly int Needed;       // XP the current bar needs to give a point
        public readonly int Delta;
        public readonly bool PointEarned;

        public XpChangedEvent(int points, int progress, int needed, int delta, bool pointEarned)
        {
            Points = points;
            Progress = progress;
            Needed = needed;
            Delta = delta;
            PointEarned = pointEarned;
        }
    }
}