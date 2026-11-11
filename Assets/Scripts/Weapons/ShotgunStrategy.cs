namespace Weapons
{
    /// <summary>
    /// The BIG BAD weapon. Mucho damage. It's different from the pistol by a couple of things:
    /// Racks after every shot (as shotguns do).
    /// Can't reload. Instead, it regains one shell every X seconds, but ONLY while unequipped.
    /// Reload time progress is paused when it is equipped, so switching weapons doesn't restart the reload timer.
    /// </summary>
    public class ShotgunStrategy : WeaponBase
    {
        private bool _equipped;
        private float _rechargeProgress;

        public ShotgunStrategy(WeaponConfig config, WeaponContext context, GunViewmodel view)
            : base(config, context, view) { }

        public override bool CanSelect => Ammo > 0;
        public override bool IsDepleted => Ammo <= 0;
        public override float DepletedHoldTime => Config.depletedHoldTime;
        public override float Recharge01 => _rechargeProgress / Config.rechargeInterval;

        protected override bool RacksAfterFire => true;

        public override void Enter()
        {
            base.Enter();
            _equipped = true;
        }

        public override void Exit()
        {
            _equipped = false;
        }

        public override void Tick(float deltaTime)
        {
            if (_equipped) return; // pause while in use

            if (Ammo >= MaxAmmo)
            {
                _rechargeProgress = 0f;
                return;
            }

            _rechargeProgress += deltaTime;
            if (_rechargeProgress < Config.rechargeInterval) return;

            _rechargeProgress -= Config.rechargeInterval;
            AddAmmo(1);
        }
    }
}