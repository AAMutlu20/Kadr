using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// The numbers that make one enemy type different from another. One asset per type
    /// (Assets > Create > Kadr > Enemy Stats), assigned on the enemy's EnemyController.
    /// Hit points are NOT here: they live on the enemy's Health component.
    ///
    /// All distances are in Unity units, where the player is 4 units tall and walks at 9 units/second.
    /// </summary>
    [CreateAssetMenu(menuName = "Kadr/Enemy Stats", fileName = "EnemyStats")]
    public class EnemyStats : ScriptableObject
    {
        [Tooltip("Sent with EnemyKilledEvent so quests and XP can tell enemy types apart.")]
        public string enemyId = "Zombie";

        [Header("Movement")]
        [Tooltip("Walking speed. The player walks at 9, so about 3.5 makes an enemy clearly slower.")]
        public float moveSpeed = 3.5f;
        [Tooltip("Speed of the opening sprint when the enemy notices the player. 0 or less than the walking speed means it never sprints.")]
        public float sprintSpeed;
        [Tooltip("Seconds the opening sprint lasts. After that the enemy walks at Move Speed.")]
        public float sprintDuration = 3f;
        [Tooltip("Degrees per second the enemy can turn while attacking.")]
        public float turnSpeed = 240f;

        /// <summary>The fastest this enemy ever moves. The Animator's Speed parameter is scaled by this.</summary>
        public float MaxSpeed => Mathf.Max(moveSpeed, sprintSpeed);

        /// <summary>True if this enemy has an opening sprint at all.</summary>
        public bool CanSprint => sprintSpeed > moveSpeed && sprintDuration > 0f;

        [Header("Senses")]
        [Tooltip("An idle enemy notices the player inside this range, if it has a clear line of sight.")]
        public float detectionRange = 25f;
        [Tooltip("A chasing enemy gives up when the player gets further away than this.")]
        public float loseRange = 45f;
        [Tooltip("A chasing enemy gives up after this many seconds without seeing the player.")]
        public float loseSightTime = 4f;

        [Header("Attack")]
        [Tooltip("Centre-to-centre distance (flat) at which the attack starts. Player radius is 1.")]
        public float attackRange = 3f;
        public float attackDamage = 10f;
        [Tooltip("Seconds between the start of one attack and the start of the next.")]
        public float attackCooldown = 1.6f;
        [Tooltip("Seconds after the attack starts at which the hit lands. Match it to the swing frame in the clip.")]
        public float attackHitDelay = 0.6f;
        [Tooltip("Tick when the attack clip has a DealDamage Animation Event. The hit then lands on that frame and the delay above is ignored.")]
        public bool hitFromAnimationEvent;
        [Tooltip("Flat distance at which the hit still lands, checked when it lands. 0 = Attack Range x 1.25 (as before). " +
                 "Make it bigger than Attack Range, so an enemy that starts a swing still hits a player who walks back a step.")]
        public float attackReach;
        [Tooltip("Largest height difference (between the feet) at which the enemy can still attack or hit. Stops attacks through floors. " +
                 "Raise it if stairs or ramps make enemies lose you.")]
        public float attackVerticalReach = 2.5f;
        [Tooltip("The enemy steps this many units towards the player during the swing, so walking away does not dodge it. 0 = no lunge.")]
        public float lungeDistance;
        [Tooltip("Seconds the lunge takes, counted from the start of the attack. 0 = Attack Hit Delay.")]
        public float lungeSeconds;

        [Header("Hit zones (shots)")]
        [Tooltip("Damage multiplier for a head hit. Zombie 1.5, crawler 1.2.")]
        public float headDamageScale = 1.5f;
        [Tooltip("Damage multiplier for a leg hit.")]
        public float legDamageScale = 0.5f;
        [Tooltip("Speed multiplier once the legs have been hit (walking and sprinting). Zombie 0.3, crawler 0.5.")]
        public float legSlowScale = 0.3f;
        [Tooltip("Leg hits needed to cancel the sprint for good. 0 = legs never cancel the sprint. Crawler 2.")]
        public int legHitsToStopSprint;
        [Tooltip("Only for enemies without HitZone colliders: a hit above this share of the body height is a head hit.")]
        [Range(0f, 1f)] public float headHeight = 0.8f;
        [Tooltip("Only for enemies without HitZone colliders: a hit below this share of the body height is a leg hit.")]
        [Range(0f, 1f)] public float legHeight = 0.35f;

        [Header("Camera flash")]
        [Tooltip("1 = full stun time, 0.5 = half (bosses), 0 = immune.")]
        public float flashStunScale = 1f;
        [Tooltip("Tick for bosses: Disorient does nothing to them.")]
        public bool immuneToDisorient;
        [Tooltip("Tick for enemies that cannot burn.")]
        public bool immuneToFire;

        [Header("Corpse")]
        [Tooltip("Seconds the body stays before the object is destroyed.")]
        public float corpseLifetime = 10f;
    }
}