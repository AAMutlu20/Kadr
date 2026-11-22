using Core;
using Events;
using Items;
using UnityEngine;

namespace Weapons
{
    /// <summary>
    /// The good old le pistol. Needs to be reloaded from time to time.
    /// Reload is triggered by R or by pulling the trigger on an empty magazine.
    /// It can also aim (hold right mouse): the spread tightens to Config.aimSpreadAngle.
    ///
    /// Reloading takes the rounds from the suitcase (AmmoReserve), in the kind the player selected with X.
    /// A magazine holds one kind at a time. Reloading with a different kind than what is loaded first puts the loaded
    /// rounds back into the suitcase, and is refused if they would not all fit. Incendiary rounds set enemies on fire.
    /// Without an AmmoReserve in the scene the pistol reloads for free, as it used to.
    /// </summary>
    public class PistolStrategy : WeaponBase
    {
        private const float WarnInterval = 1f;

        private bool _reloading;
        private bool _aiming;
        private AmmoKind _loaded = AmmoKind.Normal;
        private AmmoKind _incoming = AmmoKind.Normal;
        private float _nextWarn;

        public PistolStrategy(WeaponConfig config, WeaponContext context, GunViewmodel view)
            : base(config, context, view)
        {
            // Lives as long as the strategy, so there is no need to unsubscribe.
            view.ReloadFinished += OnReloadFinished;
        }

        public override bool IsBusy => _reloading;
        public override bool CanAim => true;
        public override bool IsAiming => _aiming;

        /// <summary>What is in the magazine now.</summary>
        public AmmoKind LoadedKind => _loaded;

        protected override bool IncendiaryRounds => _loaded == AmmoKind.Incendiary;

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
            if (_reloading) return;

            var reserve = AmmoReserve.Instance;
            if (reserve == null)
            {
                // No ammo system in the scene: the old behaviour, a free reload.
                if (Ammo >= MaxAmmo) return;

                _incoming = _loaded;
                StartReload();
                return;
            }

            var wanted = reserve.Selected;
            var swapping = wanted != _loaded && Ammo > 0;

            if (wanted == _loaded && Ammo >= MaxAmmo) return;

            if (reserve.Count(wanted) <= 0)
            {
                Warn("No " + reserve.Label(wanted) + " ammo in the suitcase");
                return;
            }

            // A different kind: the loaded rounds go back into the suitcase first, all or nothing.
            if (swapping)
            {
                var back = Ammo;
                var left = reserve.Give(_loaded, back);
                if (left > 0)
                {
                    reserve.Take(_loaded, back - left); // undo the part that did fit
                    Warn("The suitcase is full, cannot unload the magazine");
                    return;
                }

                SetAmmo(0);
            }

            _incoming = wanted;
            StartReload();
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
            _loaded = AmmoKind.Normal; // every run starts with a full magazine of normal rounds
            base.Refill();
        }

        protected override void OnDryFire() => Reload();

        private void StartReload()
        {
            SetAiming(false); // reloading always drops the sights
            _reloading = true;
            View.PlayReload();
        }

        private void OnReloadFinished()
        {
            if (!_reloading) return; // reload was cancelled, don't refill

            _reloading = false;

            var reserve = AmmoReserve.Instance;
            if (reserve == null)
            {
                SetAmmo(MaxAmmo);
                return;
            }

            // Take what is there at this moment, up to a full magazine.
            var got = reserve.Take(_incoming, MaxAmmo - Ammo);
            if (got <= 0) return;

            _loaded = _incoming;
            SetAmmo(Ammo + got);
        }

        private void Warn(string message)
        {
            if (Time.time < _nextWarn) return; // pulling the trigger on an empty gun would repeat it every click

            _nextWarn = Time.time + WarnInterval;
            if (GameServices.TryGet(out EventBus bus)) bus.Publish(new NoticeEvent(message));
        }
    }
}