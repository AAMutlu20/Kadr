namespace Weapons
{
    /// <summary>
    /// Strategy
    /// Holds the current weapons strategy (behaviour), forwards input to it and contains generic weapon rules.
    /// One implementation per weapon, each owning its own rules.
    /// Adding a new weapon is done by adding one class here and one line in WeaponController.Awake.
    /// It becomes a C# object created by WeaponController.Awake, and is kept trough the run.
    /// </summary>
    public interface IWeaponStrategy
    {
        string Name { get; }
        GunViewmodel View { get; }

        int Ammo { get; }
        int MaxAmmo { get; }

        /// <summary>True when the player can switch to their weapon at the given momment.</summary>
        bool CanSelect { get; }

        /// <summary>True while the weapon is mid-action and must not be interrupted by lowering.</summary>
        bool IsBusy { get; }

        /// <summary>
        /// True when the weapon is empty AND has can't reload, so the controller
        /// should fall back to the default weapon afterward.
        /// </summary>
        bool IsDepleted { get; }

        /// <summary>How long to keep the empty weapon up so its last animation can finish.</summary>
        float DepletedHoldTime { get; }

        /// <summary>Progress of the passive recharge, for a HUD bar. Set at 0 for weapons without recharge.</summary>
        float Recharge01 { get; }

        /// <summary>Whether this weapon has an aimed mode at all (pistol yes, shotgun no).</summary>
        bool CanAim { get; }

        bool IsAiming { get; }

        /// <summary>Starts or stops aiming. Ignored by weapons that cannot aim.</summary>
        void SetAiming(bool aiming);

        /// <summary>Called after the weapon's view is active and equipped.</summary>
        void Enter();

        /// <summary>Called when the weapon is being put away. Must cancel anything in progress.</summary>
        void Exit();

        /// <summary>Called when a lowered weapon is raised again (through the camera system), re-applies the draw delay.</summary>
        void Raised();

        void Tick(float deltaTime);

        void PrimaryFire();
        void Reload();

        /// <summary>Refills the ammo on a weapon (on run start, or on an ammo cache).</summary>
        void Refill();

        void AddAmmo(int amount);
    }
}