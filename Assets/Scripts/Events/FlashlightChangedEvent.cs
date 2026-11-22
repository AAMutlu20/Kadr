namespace Events
{
    /// <summary>The flashlight's state changed: charge 0..1, on or off, and whether the player has one at all.</summary>
    public readonly struct FlashlightChangedEvent : IGameEvent
    {
        public readonly float Charge;
        public readonly bool IsOn;
        public readonly bool Owned;

        public FlashlightChangedEvent(float charge, bool isOn, bool owned)
        {
            Charge = charge;
            IsOn = isOn;
            Owned = owned;
        }
    }
}