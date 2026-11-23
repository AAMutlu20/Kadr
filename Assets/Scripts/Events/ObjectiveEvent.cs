namespace Events
{
    /// <summary>
    /// The current task changed (or was completed). The HUD types it in the top left, holds it for a few seconds and
    /// fades it. Publish it from a trigger, a pickup, the boss room, anything:
    ///   GameServices.Get&lt;EventBus&gt;().Publish(new ObjectiveEvent("TASK 02", "Reach the observation room"));
    /// </summary>
    public readonly struct ObjectiveEvent : IGameEvent
    {
        public readonly string Title;
        public readonly string Text;
        public readonly bool Completed;

        public ObjectiveEvent(string title, string text, bool completed = false)
        {
            Title = title;
            Text = text;
            Completed = completed;
        }
    }
}