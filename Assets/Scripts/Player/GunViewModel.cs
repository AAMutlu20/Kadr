using UnityEngine;
using UnityEngine.InputSystem;

public class GunViewmodel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController body;
    [SerializeField] private Camera cam;

    [Header("Gun")]
    [SerializeField] private int magSize = 12;
    [SerializeField] private bool canReload = true; // shotgun: off
    [SerializeField] private bool racksAfterFire;  // shotgun: on
    [SerializeField] private float fireCooldown = 0.18f; // shotgun: (Fire length x Fire->Rack exit time) + Rack length
    [SerializeField] private float range = 100f;
    [SerializeField] private float damage = 25f; // per pellet
    [SerializeField] private int pellets = 1; // shotgun: ~8
    [SerializeField] private float spreadAngle; // shotgun: ~4
    [SerializeField] private float equipTime = 0.5f; // can't fire until the draw/raise finishes
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Locomotion")]
    [SerializeField] private float runSpeed = 6f; // match your controller's sprint speed

    [Header("Debug")]
    [SerializeField] private bool debugLowerKey = true; // Q toggles lowered, TODO turn off once the camera handles it

    // Parameters
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int FireHash = Animator.StringToHash("Fire");
    private static readonly int ReloadHash = Animator.StringToHash("Reload");
    private static readonly int LoweredHash = Animator.StringToHash("Lowered");
    private static readonly int HasRackHash = Animator.StringToHash("HasRack");

    // State names (must match the Animator exactly)
    private static readonly int EquipStateHash    = Animator.StringToHash("Equip");
    private static readonly int LoweringStateHash = Animator.StringToHash("Lowering");

    private int _ammo;
    private bool _initialized;
    private float _nextFireTime;
    private bool _isReloading;
    private bool _isLowered;

    public int Ammo
    {
        get { EnsureInit(); return _ammo; }
    }
    public int MagSize => magSize;
    public bool IsReloading => _isReloading;
    public bool IsLowered => _isLowered;

    private void Awake()
    {
        if (!animator) animator = GetComponent<Animator>();
        EnsureInit();
    }

    // Inactive objects don't run Awake, so ammo is set up the first time anything asks for it
    private void EnsureInit()
    {
        if (_initialized) return;
        _ammo = magSize;
        _initialized = true;
    }

    private void OnEnable()
    {
        _nextFireTime = Time.time + equipTime;
        _isLowered = false;

        if (!animator) return;
        animator.SetBool(LoweredHash, false);
        animator.SetBool(HasRackHash, racksAfterFire);
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
            else if (canReload) StartReload();
            // else: empty and can't reload, so a dry-click sound would go here
        }

        if (canReload && kb.rKey.wasPressedThisFrame && _ammo < magSize)
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
                // hit.collider.GetComponentInParent<YourHealthScript>()?.TakeDamage(damage);
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
        if (!_isReloading) return; // reload was cancelled, don't refill
        _ammo = magSize;
        _isReloading = false;
    }

    // Fills the gun completely (e.g. on run start or from an ammo cache)
    public void Refill()
    {
        EnsureInit();
        _ammo = magSize;
        _isReloading = false;
    }

    // Used by the shotgun's passive recharge
    public void AddAmmo(int amount)
    {
        EnsureInit();
        _ammo = Mathf.Min(_ammo + amount, magSize);
    }

    // Called by the camera system / weapon switcher.
    // force = true cancels a reload in progress (used when switching weapons).
    public void SetLowered(bool lowered, bool force = false)
    {
        if (lowered == _isLowered) return;

        if (lowered && _isReloading)
        {
            if (!force) return; // don't interrupt a reload unless forced
            _isReloading = false; // cancelled: no refill
        }

        _isLowered = lowered;
        animator.SetBool(LoweredHash, lowered);

        if (!lowered)
            _nextFireTime = Time.time + equipTime; // short delay while the gun comes back up
    }

    // True once the lowering animation has fully played out
    public bool IsLoweringFinished()
    {
        if (animator.IsInTransition(0)) return false;

        var info = animator.GetCurrentAnimatorStateInfo(0);
        return info.shortNameHash == LoweringStateHash && info.normalizedTime >= 1f;
    }

    // Snaps the Animator to the first frame of Equip right away,
    // so the very first visible frame is the start of the draw
    public void PlayEquipFromStart()
    {
        animator.Play(EquipStateHash, 0, 0f);
        animator.Update(0f);
    }
}