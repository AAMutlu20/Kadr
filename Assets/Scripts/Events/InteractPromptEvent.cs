namespace Events
{
    /// <summary>
    /// Published when the thing the player is looking at changes (or its prompt text does): a pickup in reach,
    /// or nothing. The prompt view listens, so the interactor never touches any UI.
    /// </summary>
    public readonly struct InteractPromptEvent : IGameEvent
    {
        public readonly bool Visible;
        public readonly string Text;

        public InteractPromptEvent(bool visible, string text)
        {
            Visible = visible;
            Text = text;
        }
    }
}