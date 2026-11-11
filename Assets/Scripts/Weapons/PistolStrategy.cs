namespace Weapons
{
    /// <summary>
    /// The good old le pistol. Needs to be reloaded from time to time.
    /// Reload is triggered by R or by pulling the trigger on an empty magazine.
    /// It can also aim (hold right mouse): the spread tightens to Config.aimSpreadAngle.
    /// </summary>
    public class PistolStrategy : WeaponBase
    {
        private bool _reloading;
        private bool _aiming;

        public PistolStrategy(WeaponConfig config, WeaponContext context, GunViewmodel view)
            : base(config, context, view)
        {
            // Lives as long as the strategy, so there is no need to unsubscribe.
            view.ReloadFinished += OnReloadFinished;
        }

        public override bool IsBusy => _reloading;
        public override bool CanAim => true;
        public override bool IsAiming => _aiming;

        // Aiming tightens the spread (Config.aimSpreadAngle, normally 0).
        protected override float CurrentSpread => _aiming ? Config.aimSpreadAngle : Config.spreadAngle;

        public override void SetAiming(bool aiming)
        {
            if (aiming == _aiming) return;
            switch (aiming)
            {
                case true when _reloading:
                // Not while the equip or fire animation is still playing. The controller asks again
                // every frame while right mouse is held, so aiming starts the moment the arms settle.
                case true when !View.IsReadyToAim():
                    return; // can't raise the sights mid-reload
                default:
                    _aiming = aiming;
                    View.SetAiming(aiming);
                    break;
            }
        }

        public override void Reload()
        {
            if (_reloading || Ammo >= MaxAmmo) return;

            SetAiming(false); // reloading always drops the sights
            _reloading = true;
            View.PlayReload();
        }

        public override void Exit()
        {
            // Switched away mid-reload: the animation event will never fire, so cancel with no refill.
            _reloading = false;
            SetAiming(false);
        }

        public override void Refill()
        {
            _reloading = false;
            base.Refill();
        }

        protected override void OnDryFire() => Reload();

        private void OnReloadFinished()
        {
            if (!_reloading) return; // reload was cancelled, don't refill

            _reloading = false;
            SetAmmo(MaxAmmo);
        }
    }
}