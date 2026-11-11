using System.Collections;
using Core;
using Events;
using Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Weapons
{
    /// <summary>
    /// Strategy Context
    /// Reads input, holds the current WeaponStrategy and forwards to it.
    ///
    /// Nothing in this class mentions a specific weapon. The ONLY place that names the
    /// concrete strategies is the composition root.
    /// Slot 0 is the default weapon, which the controller falls back to when the current one is depleted.
    ///
    /// Switching is a coroutine: lower the old view, wait for the lowering to finish, swap the
    /// active objects, then equip the new one. While it runs, input is ignored.
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        private const int DefaultSlot = 0;

        [Header("References")]
        [SerializeField] private Camera cam;
        [SerializeField] private LayerMask hitMask = ~0; // exclude the Player layer so shots never hit them.
        [SerializeField] private GunViewmodel pistolView;
        [SerializeField] private GunViewmodel shotgunView;

        [Header("Weapon tuning")]
        [SerializeField] private WeaponConfig pistolConfig = new()
        {
            name = "Pistol", magSize = 10, fireCooldown = 0.18f, equipTime = 0.5f, range = 100f,
            damage = 25f, pellets = 1, spreadAngle = 0.5f, aimSpreadAngle = 0f
        };

        [SerializeField] private WeaponConfig shotgunConfig = new()
        {
            name = "Shotgun", magSize = 8, fireCooldown = 2.1f, equipTime = 0.8f, range = 50f,
            damage = 12f, pellets = 8, spreadAngle = 4f,
            rechargeInterval = 30f, depletedHoldTime = 2.1f
        };

        [Header("Aiming (hold right mouse)")]
        [Tooltip("Optional. While aiming the player can't sprint. Leave empty to skip that.")]
        [SerializeField] private PlayerController player;
        [Tooltip("Walking speed while aiming, as a fraction of normal (0.6 = 60%). If you change it, set the Aim_Walk threshold in the Aim blend tree to 9 x this / 14.")]
        [SerializeField] private float aimMoveMultiplier = 0.6f;
        [Tooltip("Degrees the camera's field of view narrows while aiming.")]
        [SerializeField] private float aimFovReduction = 10f;
        [Tooltip("Seconds to go from hip to fully aimed (and back).")]
        [SerializeField] private float aimTransitionTime = 0.15f;

        [Header("Switching")]
        [SerializeField] private float maxLowerWait = 3f; // for safety, incase the lowering never reports finished

        [Header("Debug")]
        [SerializeField] private bool debugLowerKey = true; // Q toggles lowered, TODO remove once the camera handles it

        private IWeaponStrategy[] _weapons;
        private int _current;
        private bool _switching;
        private float _depletedTime;
        private float _baseFov;
        private float _aimBlend; // 0 = hip, 1 = fully aimed

        public IWeaponStrategy Current => _weapons[_current];
        public bool IsSwitching => _switching;

        private void Awake()
        {
            if (!cam) cam = Camera.main;
            if (!cam) return;
            _baseFov = cam.fieldOfView;

            var context = new WeaponContext(cam, hitMask, transform.root.gameObject);

            // Composition root: the only place that knows which weapons exist.
            _weapons = new IWeaponStrategy[]
            {
                new PistolStrategy(pistolConfig, context, pistolView),
                new ShotgunStrategy(shotgunConfig, context, shotgunView)
            };
        }

        private void Start()
        {
            // Every weapon starts full each run. Refill also publishes AmmoChangedEvent for the HUD.
            foreach (var weapon in _weapons)
            {
                weapon.Refill();
                weapon.View.gameObject.SetActive(false);
            }

            var start = _weapons[DefaultSlot];
            start.View.gameObject.SetActive(true);
            start.View.PlayEquipFromStart();
            start.Enter();
            PublishSwitched(start);
        }

        private void Update()
        {
            foreach (var weapon in _weapons)
            {
                weapon.Tick(Time.deltaTime);
            }

            // Before the early returns below, so aiming always drops while switching.
            UpdateAiming();

            if (_switching) return;

            // When depleted, let the last shot play out, then drop back to the default.
            if (_current != DefaultSlot && Current.IsDepleted)
            {
                _depletedTime += Time.deltaTime;
                if (_depletedTime >= Current.DepletedHoldTime)
                {
                    TrySelect(DefaultSlot);
                }

                return;
            }

            HandleInput();
        }

        private void HandleInput()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // Number keys to select slots directly
            for (var i = 0; i < _weapons.Length && i < 9; i++)
            {
                if (!kb[Key.Digit1 + i].wasPressedThisFrame) continue;
                TrySelect(i);
                return;
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.1f)
            {
                SelectNext(scroll > 0f ? 1 : -1);
                return;
            }

            if (debugLowerKey && kb.qKey.wasPressedThisFrame)
            {
                SetLowered(!Current.View.IsLowered);
            }

            if (Current.View.IsLowered) return;

            if (mouse.leftButton.wasPressedThisFrame) Current.PrimaryFire();
            if (kb.rKey.wasPressedThisFrame) Current.Reload();
        }

        /// <summary>
        /// Hold right mouse to aim. The strategy decides what aiming means for its weapon
        /// (the shotgun can't aim, so this does nothing there). The controller only owns the
        /// generic parts: the input, the FOV zoom and the sprint lock.
        /// Aiming is refused while switching, reloading or lowered.
        /// </summary>
        private void UpdateAiming()
        {
            var mouse = Mouse.current;
            var wantsAim = mouse != null && mouse.rightButton.isPressed
                           && !_switching
                           && Current.CanAim
                           && !Current.IsBusy
                           && !Current.View.IsLowered;

            if (wantsAim != Current.IsAiming)
            {
                Current.SetAiming(wantsAim);
            }

            if (player)
            {
                player.CanSprint = !Current.IsAiming;
                player.WeaponSpeedMultiplier = Current.IsAiming ? aimMoveMultiplier : 1f;
            }

            var target = Current.IsAiming ? 1f : 0f;
            _aimBlend = Mathf.MoveTowards(_aimBlend, target, Time.deltaTime / aimTransitionTime);
            cam.fieldOfView = Mathf.Lerp(_baseFov, _baseFov - aimFovReduction, Mathf.SmoothStep(0f, 1f, _aimBlend));
        }

        /// <summary>Called by the camera system to lower or raise the current weapon.</summary>
        public void SetLowered(bool lowered)
        {
            if (_switching || lowered == Current.View.IsLowered) return;
            if (lowered && Current.IsBusy) return; // don't interrupt a reload

            Current.View.SetLowered(lowered);
            if (!lowered) Current.Raised();
        }

        public bool TrySelect(int slot)
        {
            if (_switching || slot == _current) return false;
            if (slot < 0 || slot >= _weapons.Length) return false;
            if (!_weapons[slot].CanSelect) return false; // TODO an empty "click" sound to go here

            StartCoroutine(SwitchRoutine(slot));
            return true;
        }

        private void SelectNext(int direction)
        {
            for (var step = 1; step < _weapons.Length; step++)
            {
                var slot = ((_current + direction * step) % _weapons.Length + _weapons.Length) % _weapons.Length;
                if (TrySelect(slot)) return;
            }
        }

        private IEnumerator SwitchRoutine(int slot)
        {
            _switching = true;

            var from = Current;
            var to = _weapons[slot];

            // Put the old weapon away. Exit cancels any reload in progress.
            from.Exit();
            from.View.SetLowered(true);

            var waited = 0f;
            while (!from.View.IsLoweringFinished() && waited < maxLowerWait)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            from.View.gameObject.SetActive(false);

            // Show the new one already posed at the first frame of Equip
            to.View.gameObject.SetActive(true);
            to.View.PlayEquipFromStart();
            to.Enter();

            _current = slot;
            _depletedTime = 0f;
            _switching = false;

            PublishSwitched(to);
        }

        private static void PublishSwitched(IWeaponStrategy weapon)
        {
            if (GameServices.TryGet(out EventBus bus))
            {
                bus.Publish(new WeaponSwitchedEvent(weapon.Name));
            }
        }
    }
}