using System;
using UnityEngine;
using VFX;

namespace Weapons
{
    /// <summary>
    /// Plays the first-person arms/gun animations and spawns the VFX.
    /// The only thing that goes back to the strategies is the reload clip calls via an Animation Event.
    /// </summary>
    public class GunViewmodel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private CharacterController body; // only read for walk/run blending

        [Header("VFX")]
        [SerializeField] private ParticleSystem muzzleFlash;
        [SerializeField] private MuzzleLight muzzleLight;
        [SerializeField] private GameObject impactPrefab;
        [SerializeField] private GameObject bulletHolePrefab;

        [Header("Aim pose")]
        [Tooltip("Object that gets nudged while aiming. Use the PARENT of this object (e.g. PistolRoot), not the Animator object itself, because the Animator may overwrite its position. Left empty it defaults to the parent.")]
        [SerializeField] private Transform aimOffsetRoot;
        [Tooltip("Local offset applied while aiming. Z moves the arms away from the camera (shrinks them), Y moves them down. Tune it live in Play mode while aiming.")]
        [SerializeField] private Vector3 aimOffset = new(0f, -0.05f, -0.2f);
        [SerializeField] private float aimOffsetTime = 0.15f;

        [Header("Locomotion")]
        [SerializeField] private float runSpeed = 14f; // must match PlayerController's sprint speed

        // Parameters
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int FireHash = Animator.StringToHash("Fire");
        private static readonly int ReloadHash = Animator.StringToHash("Reload");
        private static readonly int LoweredHash = Animator.StringToHash("Lowered");
        private static readonly int HasRackHash = Animator.StringToHash("HasRack");
        private static readonly int AimingHash = Animator.StringToHash("Aiming");

        // State names (must match the Animator exactly)
        private static readonly int EquipStateHash = Animator.StringToHash("Equip");
        private static readonly int LoweringStateHash = Animator.StringToHash("Lowering");
        private static readonly int LocomotionStateHash = Animator.StringToHash("Locomotion");

        public event Action ReloadFinished;

        public bool IsLowered { get; private set; }

        private Vector3 _baseLocalPosition;
        private float _aimBlend; // 0 = hip, 1 = fully aimed
        private bool _aiming;

        private void Awake()
        {
            if (!animator) animator = GetComponent<Animator>();
            if (!aimOffsetRoot) aimOffsetRoot = transform.parent;
            if (aimOffsetRoot) _baseLocalPosition = aimOffsetRoot.localPosition;
        }

        private void OnEnable()
        {
            IsLowered = false;
            _aiming = false;
            _aimBlend = 0f;
            if (aimOffsetRoot) aimOffsetRoot.localPosition = _baseLocalPosition;
            if (!animator) return;
            animator.SetBool(LoweredHash, false);
            animator.SetBool(AimingHash, false);
        }

        private void Update()
        {
            if (!body) return;

            var v = body.velocity;
            v.y = 0f;
            animator.SetFloat(SpeedHash, Mathf.Clamp01(v.magnitude / runSpeed), 0.1f, Time.deltaTime);
        }

        // Slides the arms to the aim offset after the Animator has posed them this frame.
        private void LateUpdate()
        {
            if (!aimOffsetRoot || aimOffset == Vector3.zero) return;

            _aimBlend = Mathf.MoveTowards(_aimBlend, _aiming ? 1f : 0f, Time.deltaTime / aimOffsetTime);
            aimOffsetRoot.localPosition = _baseLocalPosition + aimOffset * Mathf.SmoothStep(0f, 1f, _aimBlend);
        }

        public void SetHasRack(bool hasRack) => animator.SetBool(HasRackHash, hasRack);

        /// <summary>
        /// True only when the arms are settled in the plain hip Locomotion state, i.e. not in the
        /// middle of Equip, Fire, Rack, Reload, Raising or any blend between states.
        /// </summary>
        public bool IsReadyToAim()
        {
            if (animator.IsInTransition(0)) return false;

            return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == LocomotionStateHash;
        }

        // Switches the Animator between the hip and aim states (Aim_Idle / Aim_Walk / Aim_Fire).
        public void SetAiming(bool aiming)
        {
            _aiming = aiming;
            animator.SetBool(AimingHash, aiming);
        }

        public void PlayFire()
        {
            animator.SetTrigger(FireHash);
            if (muzzleFlash) muzzleFlash.Play();
            if (muzzleLight) muzzleLight.Flash();
        }

        public void PlayReload() => animator.SetTrigger(ReloadHash);

        // Animation Event on the Reload clip. Name is hardcoded.
        public void OnReloadFinished() => ReloadFinished?.Invoke();

        /// <summary>Sparks and a bullet hole on world geometry.</summary>
        public void SpawnImpact(Vector3 point, Vector3 normal)
        {
            if (impactPrefab)
                Instantiate(impactPrefab, point + normal * 0.01f, Quaternion.LookRotation(normal));

            if (!bulletHolePrefab) return;

            var rot = Quaternion.LookRotation(-normal) // project into the surface
                      * Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f)); // random spin so holes don't look stamped
            Instantiate(bulletHolePrefab, point + normal * 0.05f, rot);
        }

        public void SetLowered(bool lowered)
        {
            if (lowered == IsLowered) return;

            IsLowered = lowered;
            animator.SetBool(LoweredHash, lowered);
        }

        // True once the lowering animation has fully played out
        public bool IsLoweringFinished()
        {
            if (animator.IsInTransition(0)) return false;

            var info = animator.GetCurrentAnimatorStateInfo(0);
            return info.shortNameHash == LoweringStateHash && info.normalizedTime >= 1f;
        }

        // Snaps the Animator to the first frame of Equip, so the first visible frame is the start of the draw
        public void PlayEquipFromStart()
        {
            animator.Play(EquipStateHash, 0, 0f);
            animator.Update(0f);
        }
    }
}