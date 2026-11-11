using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// First-person movement and look for the player, built on CharacterController.
    ///
    /// All distances are in standard units, where the player is 4 units tall (about 1.8 m irl), so
    /// speeds and gravity are roughly 2.2 times the usual metre-based values. There is deliberately
    /// no jumping in Kadr: the player walks, sprints and falls.
    ///
    /// The yaw turns this object and the pitch turns only CameraRoot, so the camera's arms and weapon
    /// follow the view while movement stays flat on the floor.
    ///
    /// Other systems change how the player moves through three switches, not by reaching into this class:
    /// SpeedMultiplier (the photo windup slows the player), CanSprint (aiming takes it away) and
    /// InputEnabled (death, menus, cutscenes).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Pivot at eye height that carries the camera. Only this rotates up and down.")]
        [SerializeField] private Transform cameraRoot;

        [Header("Movement (units per second)")]
        [SerializeField] private float walkSpeed = 9f;
        [SerializeField] private float sprintSpeed = 14f;
        [Tooltip("How fast the player reaches walking speed on the ground. Higher feels snappier.")]
        [SerializeField] private float groundAcceleration = 60f;
        [Tooltip("Lower than ground acceleration, so walking off a ledge commits the player to a direction.")]
        [SerializeField] private float airAcceleration = 20f;

        [Header("Gravity (units)")]
        [SerializeField] private float gravity = 22f;

        [Header("Look")]
        [Tooltip("Degrees of turn per pixel of mouse movement.")]
        [SerializeField] private float lookSensitivity = 0.1f;
        [SerializeField] private float maxPitch = 85f;

        // Pushes the controller into the floor a little each frame so isGrounded stays reliable on
        // stairs and slopes. Without it, grounded flickers while walking down a step.
        private const float GroundedStickVelocity = -4f;

        private CharacterController _controller;
        private Health _health;

        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private float _pitch;

        /// <summary>
        /// Scales walking and sprinting speed. 1 is normal. Set by systems such as the camera windup
        /// (which slows the player) so that they never need to know this class's speed values.
        /// </summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// Scales speed for the current weapon state (aiming slows the player). Separate from
        /// SpeedMultiplier so the weapon system and the camera windup never overwrite each other.
        /// </summary>
        public float WeaponSpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// When false the player walks even if sprint is held. Set by the weapon controller while aiming.
        /// Kept separate from SpeedMultiplier so the two systems never overwrite each other.
        /// </summary>
        public bool CanSprint { get; set; } = true;

        /// <summary>
        /// When false the player ignores movement and look input and only falls. Turned off on death.
        /// </summary>
        public bool InputEnabled { get; set; } = true;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<Health>();

            if (!cameraRoot)
            {
                Debug.LogError("[PlayerController] Camera Root is not assigned.", this);
            }
        }

        private void OnEnable()
        {
            // A first-person view needs the cursor captured. The editor releases it on Escape.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (_health)
            {
                _health.Died += OnDied;
            }
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_health)
            {
                _health.Died -= OnDied;
            }
        }

        private void Update()
        {
            Look();
            Move();
        }

        private void OnDied()
        {
            InputEnabled = false;
        }

        private void Look()
        {
            var delta = InputEnabled ? GameInput.LookDelta() : Vector2.zero;

            transform.Rotate(0f, delta.x * lookSensitivity, 0f);

            _pitch = Mathf.Clamp(_pitch - delta.y * lookSensitivity, -maxPitch, maxPitch);
            cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void Move()
        {
            var grounded = _controller.isGrounded;

            if (grounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = GroundedStickVelocity;
            }

            var input = InputEnabled ? GameInput.Move() : Vector2.zero;
            var wantsSprint = InputEnabled && CanSprint && GameInput.SprintHeld() && input.y > 0f;

            // Sprinting only works forward, so strafing and backpedalling stay at walking pace.
            var targetSpeed = (wantsSprint ? sprintSpeed : walkSpeed) * SpeedMultiplier * WeaponSpeedMultiplier;
            var targetVelocity = (transform.right * input.x + transform.forward * input.y) * targetSpeed;

            var acceleration = grounded ? groundAcceleration : airAcceleration;
            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity, targetVelocity, acceleration * Time.deltaTime);

            _verticalVelocity -= gravity * Time.deltaTime;

            var velocity = _horizontalVelocity + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
        }

        /// <summary>
        /// Runs once, when the component is first added in the editor. Sets up the CharacterController
        /// for Kadr's scale: 4 units tall, 2 wide, pivot at the feet so the player stands on y = 0.
        /// </summary>
        private void Reset()
        {
            var controller = GetComponent<CharacterController>();
            controller.height = 4f;
            controller.radius = 1f;
            controller.center = new Vector3(0f, 2f, 0f);
            controller.stepOffset = 1f;
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.1f;
        }
    }
}