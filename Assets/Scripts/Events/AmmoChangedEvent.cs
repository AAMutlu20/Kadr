namespace Events
{
    /// <summary>
    /// Published by a weapon strategy every time its ammo changes (shot, reload, recharge, refill).
    /// Carries the weapon name so a HUD showing both weapons can tell them apart.
    /// </summary>
    public readonly struct AmmoChangedEvent : IGameEvent
    {
        public readonly string Weapon;
        public readonly int Current;
        public readonly int Max;

        public AmmoChangedEvent(string weapon, int current, int max)
        {
            Weapon = weapon;
            Current = current;
            Max = max;
        }
    }
}