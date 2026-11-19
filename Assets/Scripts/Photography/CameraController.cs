using System;
using Core;
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

        [Header("Tuning")]
        [SerializeField] private CameraStats stats = new();
        [SerializeField] private ScoringSettings scoring = new();

        private State _state = State.Down;
        private float _timer;
        private bool _shutter;
        private float _zoomBlend;
        private Camera _photoCam;

        /// <summary>This run's photos. Run scoped: a new run creates a new controller.</summary>
        public PhotoAlbum Album { get; } = new();

        public bool IsRaised => _state != State.Down;
        public CameraStats Stats => stats;

        private void Awake()
        {
            if (!cam) cam = Camera.main;
            if (!weapons) weapons = FindAnyObjectByType<WeaponController>();
            if (!player) player = GetComponentInParent<PlayerController>();
            if (!health) health = GetComponentInParent<Health>();
            if (!inventory) inventory = GetComponentInParent<Inventory>();
        }

        private void OnDisable()
        {
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

            var held = kb.fKey.isPressed;

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
                    if (mouse.leftButton.wasPressedThisFrame) StartWindup();
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
                    if (player) player.SpeedMultiplier = stats.raisedSpeed;
                    _state = State.Ready;
                    break;
            }
        }

        private void LateUpdate()
        {
            if (!cam || Time.timeScale == 0f) return;

            // WeaponController writes the field of view every frame in Update, so the zoom is applied after it.
            _zoomBlend = Mathf.MoveTowards(_zoomBlend, IsRaised ? 1f : 0f, Time.deltaTime / 0.2f);
            if (_zoomBlend > 0f) cam.fieldOfView -= stats.zoom * Mathf.SmoothStep(0f, 1f, _zoomBlend);

            if (_shutter && _state == State.Windup)
            {
                _shutter = false;
                TakePhoto();
                if (player) player.SpeedMultiplier = stats.raisedSpeed; // free to move again, still vulnerable
                _state = State.Recover;
                _timer = stats.recoverTime;
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
            _timer = stats.raiseTime;

            if (weapons)
            {
                weapons.InputEnabled = false; // no firing, reloading or switching while the camera is up
                weapons.SetLowered(true);
            }

            if (player) player.SpeedMultiplier = stats.raisedSpeed;
            Publish(new CameraRaisedEvent(true, frameFraction));
        }

        private void Lower()
        {
            if (_state == State.Down) return;

            _state = State.Down;
            _shutter = false;
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
            if (player) player.SpeedMultiplier = stats.windupMoveSpeed;

            _state = State.Windup;
            _timer = stats.windupTime;
            if (_timer <= 0f) _shutter = true;
        }

        private void SetVulnerable(bool vulnerable)
        {
            if (health) health.DamageTakenMultiplier = vulnerable ? stats.vulnerability : 1f;
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
            PhotoScorer.Evaluate(cam, FrameRect(), sightBlockers, Album, scoring, photo);

            // Only photos that are worth something need a picture.
            if (photo.Score > 0f) photo.Image = Capture();

            var kept = Album.RegisterShot(photo);
            Publish(new PhotoTakenEvent(photo, kept));

            if (!kept && photo.Image)
            {
                Destroy(photo.Image);
                photo.Image = null;
            }
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