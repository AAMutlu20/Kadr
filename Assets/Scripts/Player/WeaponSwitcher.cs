using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class WeaponSwitcher : MonoBehaviour
{
    [Header("Weapons")]
    [SerializeField] private GunViewmodel pistol;
    [SerializeField] private GunViewmodel shotgun;

    [Header("Timing")]
    [SerializeField] private float lastShotDelay = 2.1f; // shotgun fire cooldown, so the last rack plays
    [SerializeField] private float maxLowerWait = 3f; // safety net if the lowering never reports finished

    [Header("Shotgun Recharge")]
    [SerializeField] private float rechargeInterval = 30f; // seconds per shell, only while not equipped

    private bool _shotgunActive;
    private bool _switching;
    private float _emptyTime;
    private float _rechargeProgress;

    public bool ShotgunActive => _shotgunActive;
    public bool IsSwitching => _switching;
    public GunViewmodel ActiveGun => _shotgunActive ? shotgun : pistol;
    public int ShotgunShells => shotgun.Ammo;
    public int ShotgunMaxShells => shotgun.MagSize;
    public float Recharge01 => _rechargeProgress / rechargeInterval; // for a HUD bar

    private void Start()
    {
        shotgun.gameObject.SetActive(false);
        pistol.gameObject.SetActive(true);
        pistol.PlayEquipFromStart();
    }

    private void Update()
    {
        RechargeShotgun();

        if (_switching) return;

        // Shotgun ran dry: let the last shot + rack play, then go back to the pistol
        if (_shotgunActive && shotgun.Ammo <= 0)
        {
            _emptyTime += Time.deltaTime;
            if (_emptyTime >= lastShotDelay)
                SwitchToPistol();
            return;
        }

        HandleInput();
    }

    private void HandleInput()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        var wantPistol = kb.digit1Key.wasPressedThisFrame;
        var wantShotgun = kb.digit2Key.wasPressedThisFrame;

        if (mouse != null && Mathf.Abs(mouse.scroll.ReadValue().y) > 0.1f)
        {
            if (_shotgunActive) wantPistol = true;
            else wantShotgun = true;
        }

        if (wantShotgun && !_shotgunActive) TrySwitchToShotgun();
        else if (wantPistol && _shotgunActive) SwitchToPistol();
    }

    private void RechargeShotgun()
    {
        if (_shotgunActive) return; // paused while in use, progress is kept

        if (shotgun.Ammo >= shotgun.MagSize)
        {
            _rechargeProgress = 0f;
            return;
        }

        _rechargeProgress += Time.deltaTime;
        if (!(_rechargeProgress >= rechargeInterval)) return;
        _rechargeProgress -= rechargeInterval;
        shotgun.AddAmmo(1);
    }

    public bool TrySwitchToShotgun()
    {
        if (_switching || _shotgunActive) return false;
        if (shotgun.Ammo <= 0) return false; // an empty "click" sound could go here

        StartCoroutine(SwitchRoutine(pistol, shotgun, true));
        return true;
    }

    public void SwitchToPistol()
    {
        if (_switching || !_shotgunActive) return;
        StartCoroutine(SwitchRoutine(shotgun, pistol, false));
    }

    private IEnumerator SwitchRoutine(GunViewmodel from, GunViewmodel to, bool toShotgun)
    {
        _switching = true;

        // Lower the current weapon (interrupts fire/rack/reload) and stop its input
        from.SetLowered(true, true);
        from.enabled = false;

        // Wait until the lowering animation has finished
        var waited = 0f;
        while (!from.IsLoweringFinished() && waited < maxLowerWait)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        // Hide the old weapon
        from.enabled = true; // so it works next time it's shown
        from.gameObject.SetActive(false);

        // Show the new one already posed at the first frame of Equip
        to.gameObject.SetActive(true);
        to.PlayEquipFromStart();

        _shotgunActive = toShotgun;
        _emptyTime = 0f;
        _switching = false;
    }
}