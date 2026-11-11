using System;
using UnityEngine;

namespace Weapons
{
    /// <summary>
    /// Holds the tuning numbers for a weapon, edited in the Inspector on WeaponController.
    /// All the behaviour lives in the strategy classes.
    /// </summary>
    [Serializable]
    public class WeaponConfig
    {
        public string name = "Pistol";
        public int magSize = 12;
        public float fireCooldown = 0.18f; // seconds between shots
        public float equipTime = 0.5f; // can't fire until the draw finishes
        public float range = 100f;
        public float damage = 25f; // per pellet of the shotgun
        public int pellets = 1;
        public float spreadAngle; // degrees, random per pellet (hip fire)
        public float aimSpreadAngle; // degrees while aiming, only used by weapons that can aim

        [Header("Passive recharge (weapons that don't reload)")]
        public float rechargeInterval = 30f; // seconds per shell, only while holstered
        public float depletedHoldTime = 2.1f; // keep an empty weapon up this long so the last rack plays
    }

    /// <summary>
    /// Holds all a strategy needs from the code. 
    /// Built once by WeaponController and shared by all strategies.
    /// </summary>
    public sealed class WeaponContext
    {
        public readonly Camera Cam;
        public readonly LayerMask HitMask;
        public readonly GameObject Shooter; // passed as the damage source

        public WeaponContext(Camera cam, LayerMask hitMask, GameObject shooter)
        {
            Cam = cam;
            HitMask = hitMask;
            Shooter = shooter;
        }
    }
}