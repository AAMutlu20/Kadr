using Core;
using Events;
using Photography;
using UnityEngine;
using Weapons;

namespace Gameplay
{
    /// <summary>
    /// A handheld, slightly shaky first person view, in the spirit of REC: the camera bobs with every step, sways and
    /// tilts as you move, leans into a strafe and into a sprint, and jolts when you are hit or take a photo.
    /// Put it on the Main Camera. It needs nothing from your player scripts: speed is read from how far the player moved.
    ///
    /// Made of four layers, all added together:
    ///  - STEP BOB: up and down, side to side and a little roll, once per step. Stronger when sprinting.
    ///  - HANDHELD SWAY: slow noise on the view angles, always on. Barely there when standing, wilder when sprinting.
    ///  - LEAN: roll into a strafe, a forward tilt and a wider field of view while sprinting.
    ///  - TRAUMA: a quick shake that fades out. Hits and photos add to it (AddTrauma from your own code works too).
    ///
    /// It is applied at the end of the frame and removed again at the start of the next one (a tiny hook component, added
    /// automatically, runs before everything else), so it never fights your look code, which reads and writes the camera
    /// rotation in Update. Everything is lighter while the photo camera is up and while aiming, so it never makes
    /// framing a photo or a shot unfair. The Intensity slider (and the static GlobalIntensity, for a settings menu)
    /// turns the whole thing down for players who get motion sick.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class HandheldCamera : MonoBehaviour
    {
        [System.Serializable]
        public class Bob
        {
            [Tooltip("Up and down, in units. The player is 4 units tall.")]
            public float vertical = 0.1f;
            [Tooltip("Side to side, in units.")]
            public float horizontal = 0.07f;
            [Tooltip("Tilt left and right, in degrees.")]
            public float roll = 0.7f;
            [Tooltip("Nodding, in degrees.")]
            public float pitch = 0.5f;
        }

        [System.Serializable]
        public class Sway
        {
            [Tooltip("Degrees of wander on each axis.")]
            public float degrees = 0.12f;
            [Tooltip("How fast it wanders, in cycles per second.")]
            public float speed = 0.35f;
        }

        /// <summary>Multiplies the whole effect. A settings menu can change this.</summary>
        public static float GlobalIntensity = 1f;

        [Header("References (empty = found automatically)")]
        [SerializeField] private Transform player;
        [SerializeField] private CameraController photoCamera;
        [SerializeField] private WeaponController weapons;

        [Header("Overall")]
        [SerializeField, Range(0f, 2f)] private float intensity = 1f;
        [Tooltip("Share of the effect left while the photo camera is up.")]
        [SerializeField, Range(0f, 1f)] private float cameraRaisedScale = 0.5f;
        [Tooltip("Share of the effect left while aiming the pistol.")]
        [SerializeField, Range(0f, 1f)] private float aimScale = 0.5f;

        [Header("Speeds, in units per second")]
        [Tooltip("Normal walking speed. The player walks at 9.")]
        [SerializeField] private float walkSpeed = 9f;
        [Tooltip("Sprinting speed. Set it to the real sprint speed of your PlayerController.")]
        [SerializeField] private float sprintSpeed = 15f;

        [Header("Step bob")]
        [Tooltip("Distance walked for one full left and right step. Smaller = faster steps.")]
        [SerializeField] private float distancePerCycle = 9f;
        [SerializeField] private Bob walkBob = new();
        [SerializeField] private Bob sprintBob = new() { vertical = 0.13f, horizontal = 0.05f, roll = 0.55f, pitch = 0.5f };

        [Header("Handheld sway")]
        [SerializeField] private Sway idleSway = new() { degrees = 0.12f, speed = 0.35f };
        [SerializeField] private Sway walkSway = new() { degrees = 0.35f, speed = 0.9f };
        [SerializeField] private Sway sprintSway = new() { degrees = 0.3f, speed = 1.0f };

        [Header("Lean")]
        [Tooltip("Degrees of roll when moving fully sideways.")]
        [SerializeField] private float strafeRoll = 0.8f;
        [Tooltip("Degrees the view tips forward while sprinting.")]
        [SerializeField] private float sprintPitch = 0.6f;
        [Tooltip("Degrees the field of view widens while sprinting.")]
        [SerializeField] private float sprintFovKick = 3f;

        [Header("Drunk (alcohol)")]
        [Tooltip("Extra wander in degrees at full drunkenness. It is added on top of the normal sway.")]
        [SerializeField] private float drunkWobbleDegrees = 1.4f;
        [Tooltip("How fast the drunk wobble drifts, in cycles per second.")]
        [SerializeField] private float drunkWobbleSpeed = 0.5f;

        [Header("Trauma (hits and photos)")]
        [Tooltip("Largest shake angle, in degrees, at full trauma.")]
        [SerializeField] private float traumaDegrees = 6f;
        [SerializeField] private float traumaDecay = 1.6f;
        [Tooltip("Trauma from a hit is damage divided by this, at most 0.8.")]
        [SerializeField] private float damageForFullTrauma = 40f;
        [SerializeField, Range(0f, 1f)] private float photoKick = 0.15f;

        private Camera _camera;
        private Health _health;
        private EventBus _bus;
        private Vector3 _lastPlayerPosition;
        private bool _hasLast;
        private Vector3 _velocity;       // smoothed, world space
        private float _move;             // 0..1 smoothed share of walk speed
        private float _sprint;           // 0..1 smoothed share of the way to sprint speed
        private float _scale = 1f;
        private float _phase;
        private float _swayTime;
        private float _drunkTime;
        private float _trauma;
        private float _moveVel, _sprintVel, _scaleVel;
        private Vector3 _lateralVel;
        private Vector3 _localVelocity;
        private const float MeasureWindow = 0.1f;
        private Vector3 _windowStart;
        private float _windowTime;
        private Vector3 _rawVelocity;

        private bool _applied;
        private Vector3 _appliedPosition;
        private Quaternion _appliedRotation = Quaternion.identity;

        [ContextMenu("Reset to default feel")]
        private void ResetToDefaultFeel()
        {
            walkBob = new Bob();
            sprintBob = new Bob { vertical = 0.13f, horizontal = 0.05f, roll = 0.55f, pitch = 0.5f };
            idleSway = new Sway { degrees = 0.12f, speed = 0.35f };
            walkSway = new Sway { degrees = 0.35f, speed = 0.9f };
            sprintSway = new Sway { degrees = 0.3f, speed = 1.0f };
            strafeRoll = 0.8f;
            sprintPitch = 0.6f;
            sprintFovKick = 3f;
            distancePerCycle = 9f;
        }

        /// <summary>A quick shake, 0..1. Add it from anywhere: an explosion, a heavy landing, a boss roar.</summary>
        public void AddTrauma(float amount) => _trauma = Mathf.Clamp01(_trauma + amount);

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (!player)
            {
                var controller = GetComponentInParent<PlayerController>();
                if (!controller) controller = FindAnyObjectByType<PlayerController>();
                if (controller) player = controller.transform;
            }

            if (!photoCamera) photoCamera = FindAnyObjectByType<CameraController>();
            if (!weapons) weapons = FindAnyObjectByType<WeaponController>();
            if (player) _health = player.GetComponent<Health>();

            gameObject.AddComponent<UndoHook>().Owner = this;
        }

        private void OnEnable()
        {
            if (!GameServices.TryGet(out _bus)) return;

            _bus.Subscribe<HealthChangedEvent>(OnHealthChanged);
            _bus.Subscribe<PhotoTakenEvent>(OnPhoto);
        }

        private void OnDisable()
        {
            Undo();

            if (_bus == null) return;
            _bus.Unsubscribe<HealthChangedEvent>(OnHealthChanged);
            _bus.Unsubscribe<PhotoTakenEvent>(OnPhoto);
            _bus = null;
        }

        private void OnHealthChanged(HealthChangedEvent e)
        {
            if (!_health || e.Entity != _health.gameObject || e.Delta >= 0f) return;

            AddTrauma(Mathf.Min(0.8f, -e.Delta / Mathf.Max(1f, damageForFullTrauma)));
        }

        private void OnPhoto(PhotoTakenEvent e) => AddTrauma(photoKick);

        /// <summary>Takes last frame's shake off again. Runs before all other scripts, from the hook below.</summary>
        private void Undo()
        {
            if (!_applied) return;

            transform.localRotation *= Quaternion.Inverse(_appliedRotation);
            transform.localPosition -= _appliedPosition;
            _applied = false;
        }

        private void LateUpdate()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f || !player) return; // paused: leave the view as it is

            MeasureMovement(dt);

            // Lighter while the photo camera is up or the pistol is aimed.
            var target = intensity * GlobalIntensity;
            if (photoCamera && photoCamera.IsRaised) target *= cameraRaisedScale;
            if (weapons && weapons.Current != null && weapons.Current.IsAiming) target *= aimScale;
            _scale = Mathf.SmoothDamp(_scale, target, ref _scaleVel, 0.2f);

            // The step cycle advances with distance walked, so the bob always matches the feet.
            var speed = new Vector3(_velocity.x, 0f, _velocity.z).magnitude;
            var stride = distancePerCycle * Mathf.Lerp(1f, 1.3f, _sprint); // longer strides when sprinting, so the steps do not get frantic
            _phase += speed / Mathf.Max(0.1f, stride) * Mathf.PI * 2f * dt;
            if (_phase > 1000f) _phase -= 1000f;

            var t = Time.time;
            var bob = LerpBob(walkBob, sprintBob, _sprint);
            var sway = LerpSway(LerpSway(idleSway, walkSway, _move, _swayA), sprintSway, _sprint, _swayB);

            // Step bob: two bounces per cycle (left and right foot), one side to side sweep.
            var sin = Mathf.Sin(_phase);
            var bounce = Mathf.Sin(_phase * 2f);
            var position = new Vector3(sin * bob.horizontal, Mathf.Abs(bounce) * bob.vertical - bob.vertical * 0.5f, 0f) * _move;
            var euler = new Vector3(-Mathf.Abs(bounce) * bob.pitch * _move, 0f, -sin * bob.roll * _move);

            // Handheld sway: smooth noise, different on every axis.
            // The noise position is ACCUMULATED, so changing the speed only makes it wander faster or slower. Sampling at
            // time * speed would jump to an unrelated value whenever the speed changes (every start and stop).
            _swayTime += dt * sway.speed;
            if (_swayTime > 10000f) _swayTime -= 10000f;
            euler += new Vector3(Noise(_swayTime, 1f, 1f), Noise(_swayTime, 1f, 2f), Noise(_swayTime, 1f, 3f)) * sway.degrees;

            // Drunk: a slow, large wander on top of the sway, with a lazy roll and a little drift of the position.
            // Accumulated like the sway, so the speed never makes it jump. Alcohol sets PlayerBuffs.Drunk (0..1).
            var buffs = PlayerBuffs.Instance;
            if (buffs && buffs.IsDrunk)
            {
                _drunkTime += dt * drunkWobbleSpeed;
                if (_drunkTime > 10000f) _drunkTime -= 10000f;

                var drunk = buffs.Drunk;
                euler += new Vector3(Noise(_drunkTime, 1f, 11f) * 0.7f, Noise(_drunkTime, 1f, 12f) * 0.6f, Noise(_drunkTime, 1f, 13f)) * (drunkWobbleDegrees * drunk);
                position += new Vector3(Noise(_drunkTime, 1f, 14f), Noise(_drunkTime, 1f, 15f) * 0.5f, 0f) * (0.12f * drunk);
            }

            // Lean: into a strafe, tipped forward in a sprint.
            euler.z -= Mathf.Clamp(_localVelocity.x / walkSpeed, -1.5f, 1.5f) * strafeRoll;
            euler.x += sprintPitch * _sprint;

            // Trauma: the shake grows with the square of the trauma, so small hits barely register.
            if (_trauma > 0f)
            {
                var shake = _trauma * _trauma;
                euler += new Vector3(Noise(t, 18f, 4f), Noise(t, 18f, 5f), Noise(t, 18f, 6f)) * (traumaDegrees * shake);
                position += new Vector3(Noise(t, 18f, 7f), Noise(t, 18f, 8f), 0f) * (0.12f * shake);
                _trauma = Mathf.Max(0f, _trauma - traumaDecay * dt);
            }

            position *= _scale;
            euler *= _scale;

            _appliedPosition = position;
            _appliedRotation = Quaternion.Euler(euler);
            transform.localPosition += _appliedPosition;
            transform.localRotation *= _appliedRotation;
            _applied = true;

            if (_camera && sprintFovKick > 0f) _camera.fieldOfView += sprintFovKick * _sprint * _scale;
        }

        /// <summary>
        /// Speed from how far the player actually moved, so it works with any movement script.
        /// The distance is gathered over a short window, not per frame: a player moved in FixedUpdate only changes
        /// position on physics steps, so a per-frame reading flickers between 0 and double speed.
        /// </summary>
        private void MeasureMovement(float dt)
        {
            var position = player.position;
            if (!_hasLast)
            {
                _lastPlayerPosition = position;
                _windowStart = position;
                _windowTime = 0f;
                _hasLast = true;
            }

            // A teleport (respawn, a new level) is not running at a million units a second.
            if ((position - _lastPlayerPosition).sqrMagnitude > 4f)
            {
                _windowStart = position;
                _windowTime = 0f;
                _velocity = Vector3.zero;
                _rawVelocity = Vector3.zero;
            }
            _lastPlayerPosition = position;

            _windowTime += dt;
            if (_windowTime >= MeasureWindow)
            {
                _rawVelocity = (position - _windowStart) / _windowTime;
                _windowStart = position;
                _windowTime = 0f;
            }

            _velocity = Vector3.SmoothDamp(_velocity, _rawVelocity, ref _lateralVel, 0.12f);
            _localVelocity = player.InverseTransformDirection(_velocity);

            var flat = new Vector3(_velocity.x, 0f, _velocity.z).magnitude;
            var moveTarget = Mathf.Clamp01(flat / Mathf.Max(0.1f, walkSpeed));
            var sprintTarget = Mathf.InverseLerp(walkSpeed * 1.15f, Mathf.Max(walkSpeed * 1.2f, sprintSpeed), flat);

            _move = Mathf.SmoothDamp(_move, moveTarget, ref _moveVel, 0.25f);
            _sprint = Mathf.SmoothDamp(_sprint, Mathf.Clamp01(sprintTarget), ref _sprintVel, 0.4f);
        }

        /// <summary>Smooth noise between -1 and 1. 'speed' is in cycles per second, 'seed' makes each axis different.</summary>
        private static float Noise(float time, float speed, float seed)
        {
            return Mathf.PerlinNoise(time * speed + seed * 31.7f, seed * 7.3f) * 2f - 1f;
        }

        // Scratch objects, so working out the blend does not create garbage every frame.
        private readonly Bob _bobScratch = new();
        private readonly Sway _swayA = new();
        private readonly Sway _swayB = new();

        private Bob LerpBob(Bob a, Bob b, float t)
        {
            _bobScratch.vertical = Mathf.Lerp(a.vertical, b.vertical, t);
            _bobScratch.horizontal = Mathf.Lerp(a.horizontal, b.horizontal, t);
            _bobScratch.roll = Mathf.Lerp(a.roll, b.roll, t);
            _bobScratch.pitch = Mathf.Lerp(a.pitch, b.pitch, t);
            return _bobScratch;
        }

        private static Sway LerpSway(Sway a, Sway b, float t, Sway into)
        {
            into.degrees = Mathf.Lerp(a.degrees, b.degrees, t);
            into.speed = Mathf.Lerp(a.speed, b.speed, t);
            return into;
        }

        /// <summary>Runs before every other script's Update, and takes last frame's shake off the camera.</summary>
        [DefaultExecutionOrder(-1000)]
        private sealed class UndoHook : MonoBehaviour
        {
            public HandheldCamera Owner;

            private void Update()
            {
                if (Owner) Owner.Undo();
            }
        }
    }
}