using Core;
using Events;
using Gameplay;
using UnityEngine;

namespace Weapons
{
    /// <summary>
    /// Shared plumbing for hit scan weapons, including: ammo counter, fire cooldown, raycast and a damage call.
    /// It has no opinion about reloading or recharging, because this varies per weapon, so the strategies deal with it.
    /// </summary>
    public abstract class WeaponBase : IWeaponStrategy
    {
        protected readonly WeaponConfig Config;
        protected readonly WeaponContext Context;

        private float _nextFireTime;

        protected WeaponBase(WeaponConfig config, WeaponContext context, GunViewmodel view)
        {
            Config = config;
            Context = context;
            View = view;
            Ammo = config.magSize;
        }

        public GunViewmodel View { get; }
        public string Name => Config.name;
        public int Ammo { get; private set; }
        public int MaxAmmo => Config.magSize;

        // Defaults describe a prop weapon. The strategies then override only what makes a select weapon different.
        public virtual bool CanSelect => true;
        public virtual bool IsBusy => false;
        public virtual bool IsDepleted => false;
        public virtual float DepletedHoldTime => 0f;
        public virtual float Recharge01 => 0f;
        public virtual bool CanAim => false;
        public virtual bool IsAiming => false;
        public virtual void SetAiming(bool aiming) { }

        /// <summary>Decide if the animator should use the racking transition.</summary>
        protected virtual bool RacksAfterFire => false;

        /// <summary>Spread used for the next shot. Weapons that can aim override this.</summary>
        protected virtual float CurrentSpread => Config.spreadAngle;

        public virtual void Enter()
        {
            View.SetHasRack(RacksAfterFire);
            Raised();
        }

        public virtual void Exit() { }

        public void Raised()
        {
            _nextFireTime = Time.time + Config.equipTime;
        }

        public virtual void Tick(float deltaTime) { }

        public void PrimaryFire()
        {
            if (Time.time < _nextFireTime || IsBusy) return;

            if (Ammo <= 0)
            {
                OnDryFire();
                return;
            }

            _nextFireTime = Time.time + Config.fireCooldown;
            SetAmmo(Ammo - 1);
            View.PlayFire();
            Shoot();
        }

        public virtual void Reload() { }

        public virtual void Refill() => SetAmmo(MaxAmmo);

        public void AddAmmo(int amount) => SetAmmo(Ammo + amount);

        /// <summary>Trigger pulled with an empty weapon. TODO Implement a dry-click sound later.</summary>
        protected virtual void OnDryFire() { }

        protected void SetAmmo(int value)
        {
            Ammo = Mathf.Clamp(value, 0, MaxAmmo);

            if (GameServices.TryGet(out EventBus bus))
            {
                bus.Publish(new AmmoChangedEvent(Name, Ammo, MaxAmmo));
            }
        }

        private void Shoot()
        {
            var camTransform = Context.Cam.transform;
            var spreadAngle = CurrentSpread;

            for (var i = 0; i < Config.pellets; i++)
            {
                var spread = Quaternion.Euler(
                    Random.Range(-spreadAngle, spreadAngle),
                    Random.Range(-spreadAngle, spreadAngle),
                    0f);
                var dir = camTransform.rotation * spread * Vector3.forward;

                if (!TryRaycastIgnoringShooter(camTransform.position, dir, out var hit)) continue;

                // GetComponentInParent: the collider is usually on a child of the enemy root.
                var target = hit.collider.GetComponentInParent<IDamageable>();
                if (target != null)
                {
                    target.TakeDamage(Config.damage, Context.Shooter);

                    // The target decides what being hit looks like (blood, sparks...), we only say where.
                    hit.collider.GetComponentInParent<IHitReceiver>()?.OnHit(hit.point, hit.normal);
                    continue; // living target: no bullet hole
                }

                View.SpawnImpact(hit.point, hit.normal);
            }
        }

        // Reused every shot so firing never allocates.
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];

        /// <summary>
        /// Raycast that skips anything belonging to the shooter (the player's own capsule, arms and
        /// any child collider), so a shot can never hurt the player or be blocked by the player's body,
        /// whatever the Hit Mask is set to. Returns the nearest hit on anything else.
        /// </summary>
        private bool TryRaycastIgnoringShooter(Vector3 origin, Vector3 direction, out RaycastHit nearest)
        {
            var count = Physics.RaycastNonAlloc(origin, direction, HitBuffer, Config.range,
                Context.HitMask, QueryTriggerInteraction.Ignore);

            nearest = default;
            var nearestDistance = float.MaxValue;
            var found = false;

            for (var i = 0; i < count; i++)
            {
                var candidate = HitBuffer[i];
                if (candidate.collider.transform.IsChildOf(Context.Shooter.transform)) continue;
                if (candidate.distance >= nearestDistance) continue;

                nearest = candidate;
                nearestDistance = candidate.distance;
                found = true;
            }

            return found;
        }
    }
}