namespace Events
{
    /// <summary>
    /// Published by WeaponController once a new weapon is fully equipped , so the HUD changes together with the arms.
    /// </summary>
    public readonly struct WeaponSwitchedEvent : IGameEvent
    {
        public readonly string Weapon;

        public WeaponSwitchedEvent(string weapon)
        {
            Weapon = weapon;
        }
    }
}