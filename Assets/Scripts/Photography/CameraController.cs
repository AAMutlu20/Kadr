using System;
using Core;
using Enemies;
using Events;
using Gameplay;
using Items;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using Weapons;

namespace Photography
{
    /// <summary>
    /// Numbers the camera upgrades will change later. One instance lives on the controller for now.
    /// Low tiers: a long windup and a high vulnerability. High tiers: windup 0 (instant).
    /// </summary>
    [Serializable]
    public class CameraStats
    {
        [Tooltip("Seconds to bring the camera up before you can shoot.")]
        public float raiseTime = 0.35f;
        [Tooltip("Seconds between pressing the shutter and the photo. 0 = instant.")]
        public float windupTime = 0.6f;
        [Tooltip("Seconds after the photo before you can shoot again.")]
        public float recoverTime = 0.8f;
        [Tooltip("Movement speed while the camera is up (1 = normal).")]
        public float raisedSpeed = 0.6f;
        [Tooltip("Movement speed during the windup (0 = the player stands still).")]
        public float windupMoveSpeed = 0f;
        [Tooltip("Damage taken during windup and recovery (1 = normal, 1.5 = half again).")]
        public float vulnerability = 1.5f;
        [Tooltip("Degrees the field of view narrows while the camera is up.")]
        public float zoom = 12f;
    }

    /// <summary>
    /// The photo camera. On the Player.
    ///
    /// Hold F: the weapon is lowered, the viewfinder appears (PhotoViewfinderView listens to CameraRaisedEvent).
    /// Left click: windup (slow, takes more damage), then the shutter: the shot is scored and captured, a
    /// PhotoTakenEvent goes on the bus, then a short recovery. Letting go of F during windup cancels the shot.
    ///
    /// The photo is a square crop of the centre of the screen, rendered by a hidden second camera so the weapon
    /// arms and the UI are not in it. Scoring runs once per photo (see PhotoScorer).
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        private enum State { Down, Raising, Ready, Windup, Recover }

        [Header("References (empty = found automatically)")]
        [SerializeField] private Camera cam;
        [SerializeField] private WeaponController weapons;
        [SerializeField] private PlayerController player;
        [SerializeField] private Health health;
        [SerializeField] private Inventory inventory;

        [Header("Frame and photo")]
        [Tooltip("Height of the square frame as a share of the screen height.")]
        [SerializeField, Range(0.3f, 0.9f)] private float frameFraction = 0.6f;
        [Tooltip("Side of the saved photo in pixels.")]
        [SerializeField] private int photoSize = 384;
        [Tooltip("Level geometry that blocks the view of a subject. Not the Player or Enemy layers.")]
        [SerializeField] private LayerMask sightBlockers = 1;
        [Tooltip("What the photo shows. Leave out the weapon/arms layer, the UI layer and the Inventory layer.")]
        [SerializeField] private LayerMask photoCullingMask = ~0;

        [Header("Tuning (the base camera. Upgrades change a copy of these at runtime)")]
        [SerializeField] private CameraStats stats = new();
        [SerializeField] private ScoringSettings scoring = new();

        [Header("Upgrades")]
        [Tooltip("The upgrade tree asset. Empty = a default tree made in memory. Assign the same asset everywhere.")]
        [SerializeField] private CameraUpgradeTree upgradeTree;

        [Header("Effects")]
        [Tooltip("Your fire particle system. It is spawned on every enemy hit by Fireshow, in the middle of its body. Optional.")]
        [SerializeField] private GameObject burnEffectPrefab;
        [Tooltip("Scales the fire effect, for big or small enemies.")]
        [SerializeField, Min(0.1f)] private float burnEffectScale = 1f;

        private CameraStats _stats;
        private ScoringSettings _scoring;
        private CameraLoadout _loadout = new();
        private NightVision _night;
        private float _abilityReadyAt;
        private float _abilityCooldown;

        private State _state = State.Down;
        private float _timer;
        private bool _shutter;
        private float _zoomBlend;
        private Camera _photoCam;

        /// <summary>This run's photos. Run scoped: a new run creates a new controller.</summary>
        public PhotoAlbum Album { get; } = new();

        public bool IsRaised => _state != State.Down;
        public CameraStats Stats => _stats;

        /// <summary>The equipped ability, how long until it can fire again, and its full cooldown.</summary>
        public CameraAbility EquippedAbility => CameraProgress.Current.Equipped;
        public float AbilityCooldownLeft => Mathf.Max(0f, _abilityReadyAt - Time.time);
        public float AbilityCooldownTotal => _abilityCooldown;

        private void Awake()
        {
            if (!cam) cam = Camera.main;
            if (!weapons) weapons = FindAnyObjectByType<WeaponController>();
            if (!player) player = GetComponentInParent<PlayerController>();
            if (!health) health = GetComponentInParent<Health>();
            if (!inventory) inventory = GetComponentInParent<Inventory>();

            _night = GetComponent<NightVision>();
            if (!_night) _night = gameObject.AddComponent<NightVision>();

            upgradeTree = CameraUpgradeTree.Resolve(upgradeTree);
            ApplyUpgrades();

            // One fire for the whole game: incendiary bullets use the same particle system as the Fireshow flash.
            EnemyStatus.SharedBurnEffect = burnEffectPrefab;
            EnemyStatus.SharedBurnScale = burnEffectScale;
        }

        private void OnEnable()
        {
            CameraProgress.Current.Changed += ApplyUpgrades;
        }

        /// <summary>Works out the camera's real numbers from the base values plus the owned upgrades.</summary>
        private void ApplyUpgrades()
        {
            _stats = JsonUtility.FromJson<CameraStats>(JsonUtility.ToJson(stats));
            _scoring = JsonUtility.FromJson<ScoringSettings>(JsonUtility.ToJson(scoring));
            _loadout = CameraProgress.Current.Apply(upgradeTree, _stats, _scoring);
            Album.RepeatTable = _loadout.repeatTable;
        }

        private void OnDisable()
        {
            CameraProgress.Current.Changed -= ApplyUpgrades;
            Lower();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null || !cam) return;

            // Paused (inventory open), or dead: the camera goes away.
            var blocked = Time.timeScale == 0f || (inventory && inventory.IsOpen) || (health && health.IsDead);
            if (blocked)
            {
                Lower();
                return;
            }

            var held = KadrControls.Held(GameAction.RaiseCamera);

            switch (_state)
            {
                case State.Down:
                    if (held && CanRaise()) Raise();
                    break;

                case State.Raising:
                    if (!held) { Lower(); break; }
                    _timer -= Time.deltaTime;
                    if (_timer <= 0f) _state = State.Ready;
                    break;

                case State.Ready:
                    if (!held) { Lower(); break; }
                    if (KadrControls.Pressed(GameAction.Fire)) StartWindup();
                    break;

                case State.Windup:
                    if (!held) { Lower(); break; } // cancelled
                    _timer -= Time.deltaTime;
                    if (_timer <= 0f) _shutter = true; // taken in LateUpdate, after the field of view is final
                    break;

                case State.Recover:
                    _timer -= Time.deltaTime;
                    if (_timer > 0f) break;
                    if (!held) { Lower(); break; }
                    SetVulnerable(false);
                    if (player) player.SpeedMultiplier = _stats.raisedSpeed;
                    _state = State.Ready;
                    break;
            }
        }

        private void LateUpdate()
        {
            if (!cam || Time.timeScale == 0f) return;

            // WeaponController writes the field of view every frame in Update, so the zoom is applied after it.
            _zoomBlend = Mathf.MoveTowards(_zoomBlend, IsRaised ? 1f : 0f, Time.deltaTime / 0.2f);
            if (_zoomBlend > 0f) cam.fieldOfView -= _stats.zoom * Mathf.SmoothStep(0f, 1f, _zoomBlend);

            if (_shutter && _state == State.Windup)
            {
                _shutter = false;
                TakePhoto();
                if (player) player.SpeedMultiplier = _stats.raisedSpeed; // free to move again, still vulnerable
                _state = State.Recover;
                _timer = _stats.recoverTime;
            }
        }

        // ---------- raising and lowering ----------

        private bool CanRaise()
        {
            // Never interrupt a reload or a weapon switch.
            return !weapons || (!weapons.IsSwitching && !weapons.Current.IsBusy);
        }

        private void Raise()
        {
            _state = State.Raising;
            _timer = _stats.raiseTime;

            if (weapons)
            {
                weapons.InputEnabled = false; // no firing, reloading or switching while the camera is up
                weapons.SetLowered(true);
            }

            if (player) player.SpeedMultiplier = _stats.raisedSpeed;
            Publish(new CameraRaisedEvent(true, frameFraction));
        }

        private void Lower()
        {
            if (_state == State.Down) return;

            _state = State.Down;
            _shutter = false;
            if (_night) _night.End();
            SetVulnerable(false);
            if (player) player.SpeedMultiplier = 1f;

            if (weapons)
            {
                weapons.InputEnabled = true;
                weapons.SetLowered(false);
            }

            Publish(new CameraRaisedEvent(false, frameFraction));
        }

        private void StartWindup()
        {
            SetVulnerable(true);
            if (player) player.SpeedMultiplier = _stats.windupMoveSpeed;

            _state = State.Windup;
            _timer = _stats.windupTime;
            if (_timer <= 0f) _shutter = true;
        }

        private void SetVulnerable(bool vulnerable)
        {
            if (health) health.DamageTakenMultiplier = vulnerable ? _stats.vulnerability : 1f;
        }

        // ---------- the photo ----------

        /// <summary>The square frame in viewport coordinates (0..1).</summary>
        private Rect FrameRect()
        {
            var height = frameFraction;
            var width = height * cam.pixelHeight / cam.pixelWidth;
            return new Rect(0.5f - width * 0.5f, 0.5f - height * 0.5f, width, height);
        }

        private void TakePhoto()
        {
            var photo = new PhotoData();
            PhotoScorer.Evaluate(cam, FrameRect(), sightBlockers, Album, _scoring, photo);

            // Only photos that are worth something need a picture.
            if (photo.Score > 0f) photo.Image = Capture();

            var kept = Album.RegisterShot(photo);
            Publish(new PhotoTakenEvent(photo, kept));

            FireAbility(photo);

            if (!kept && photo.Image)
            {
                Destroy(photo.Image);
                photo.Image = null;
            }
        }

        /// <summary>
        /// The equipped flash ability goes off after the photo is scored and captured, so it never changes that photo.
        /// It only uses up its cooldown if it did something. A photo while it is cooling down is a plain flash.
        /// </summary>
        private void FireAbility(PhotoData photo)
        {
            var ability = CameraProgress.Current.Equipped;
            if (ability == CameraAbility.None || Time.time < _abilityReadyAt) return;

            var level = upgradeTree.Level(ability, CameraProgress.Current.AbilityLevel(ability));
            var data = upgradeTree.Ability(ability);
            if (level == null || data == null) return;

            var used = false;

            if (ability == CameraAbility.Reveal)
            {
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                _night.Begin(level.duration);
                used = true;
            }
            else
            {
                used = FlashAbilities.Apply(ability, data, level, photo, gameObject, burnEffectPrefab, burnEffectScale) > 0;
            }

            if (!used) return;

            _abilityCooldown = level.cooldown * _loadout.abilityCooldownMultiplier;
            _abilityReadyAt = Time.time + _abilityCooldown;
        }

        private Texture2D Capture()
        {
            if (!_photoCam)
            {
                var go = new GameObject("PhotoCamera");
                go.transform.SetParent(cam.transform, false);
                _photoCam = go.AddComponent<Camera>();
            }

            _photoCam.CopyFrom(cam);
            _photoCam.enabled = false; // only ever rendered by hand
            _photoCam.transform.localPosition = Vector3.zero;
            _photoCam.transform.localRotation = Quaternion.identity;
            _photoCam.cullingMask = photoCullingMask;
            _photoCam.aspect = 1f;

            // A square that shows exactly what is inside the frame: a narrower vertical field of view.
            var tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * frameFraction;
            _photoCam.fieldOfView = 2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg;

            _photoCam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            var rt = RenderTexture.GetTemporary(photoSize, photoSize, 24);
            _photoCam.targetTexture = rt;
            _photoCam.Render();
            _photoCam.targetTexture = null;

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(photoSize, photoSize, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, photoSize, photoSize), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return image;
        }

        private static void Publish<T>(T evt) where T : struct, IGameEvent
        {
            if (GameServices.TryGet(out EventBus bus)) bus.Publish(evt);
        }
    }
}
