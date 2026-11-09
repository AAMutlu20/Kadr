using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    public class GunViewmodel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private CharacterController body;
        [SerializeField] private Camera cam;

        [Header("Gun")]
        [SerializeField] private int magSize = 12;
        [SerializeField] private float fireCooldown = 0.18f;
        [SerializeField] private float range = 100f;
        [SerializeField] private float damage = 25f;
        [SerializeField] private int pellets = 1;          // shotgun: ~8
        [SerializeField] private float spreadAngle;        // shotgun: ~4
        [SerializeField] private float equipTime = 0.5f;   // can't fire until the draw/raise finishes
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Locomotion")]
        [SerializeField] private float runSpeed = 6f; // match controller sprint speed

        [Header("Debug")]
        [SerializeField] private bool debugLowerKey = true; // Q toggles lowered for now, I'll turn it off once the camera handles it 

        private static readonly int SpeedHash   = Animator.StringToHash("Speed");
        private static readonly int FireHash    = Animator.StringToHash("Fire");
        private static readonly int ReloadHash  = Animator.StringToHash("Reload");
        private static readonly int LoweredHash = Animator.StringToHash("Lowered");

        private int _ammo;
        private float _nextFireTime;
        private bool _isReloading;
        private bool _isLowered;

        public int Ammo => _ammo;
        public int MagSize => magSize;
        public bool IsReloading => _isReloading;
        public bool IsLowered => _isLowered;

        private void Awake()
        {
            if (!animator) animator = GetComponent<Animator>();
            _ammo = magSize;
        }

        private void OnEnable()
        {
            _nextFireTime = Time.time + equipTime;
            _isLowered = false;
            if (animator != null) animator.SetBool(LoweredHash, false);
        }

        private void OnDisable()
        {
            // If switched away mid-reload, the animation event never fires, so reset here
            _isReloading = false;
        }

        private void Update()
        {
            UpdateLocomotion();

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (debugLowerKey && kb.qKey.wasPressedThisFrame)
                SetLowered(!_isLowered);

            if (_isReloading || _isLowered) return;

            if (mouse.leftButton.wasPressedThisFrame && Time.time >= _nextFireTime)
            {
                if (_ammo > 0) Fire();
                else StartReload();
            }

            if (kb.rKey.wasPressedThisFrame && _ammo < magSize)
                StartReload();
        }

        private void UpdateLocomotion()
        {
            if (!body) return;

            var v = body.velocity;
            v.y = 0f;
            animator.SetFloat(SpeedHash, Mathf.Clamp01(v.magnitude / runSpeed), 0.1f, Time.deltaTime);
        }

        private void Fire()
        {
            _ammo--;
            _nextFireTime = Time.time + fireCooldown;
            animator.SetTrigger(FireHash);

            for (var i = 0; i < pellets; i++)
            {
                var spread = Quaternion.Euler(
                    Random.Range(-spreadAngle, spreadAngle),
                    Random.Range(-spreadAngle, spreadAngle),
                    0f);
                var dir = cam.transform.rotation * spread * Vector3.forward;

                if (Physics.Raycast(cam.transform.position, dir, out var hit, range, hitMask, QueryTriggerInteraction.Ignore))
                {
                    Debug.Log($"Hit {hit.collider.name}");
                    // hit.collider.GetComponentInParent<HPScript>()?.TakeDamage(damage);
                }
            }
        }

        private void StartReload()
        {
            _isReloading = true;
            animator.SetTrigger(ReloadHash);
        }

        // Called by the animation event on the Reload clip
        public void OnReloadFinished()
        {
            _ammo = magSize;
            _isReloading = false;
        }

        // Called by the camera system / weapon switcher
        public void SetLowered(bool lowered)
        {
            if (lowered == _isLowered) return;
            if (lowered && _isReloading) return; // don't interrupt a reload

            _isLowered = lowered;
            animator.SetBool(LoweredHash, lowered);

            if (!lowered)
                _nextFireTime = Time.time + equipTime; // short delay while the gun comes back up
        }
    }
}