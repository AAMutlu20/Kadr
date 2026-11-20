using UnityEngine;

namespace Enemies
{
    public enum BodyZone
    {
        Torso,
        Head,
        Legs
    }

    /// <summary>
    /// Put this on a collider that is a body part of an enemy (a child of the enemy, usually on a bone) and set
    /// the zone. A shot that hits this collider counts as a hit on that zone. Enemies without these colliders
    /// fall back to the height of the hit point, see EnemyStats (Head Height / Leg Height).
    /// </summary>
    public class HitZone : MonoBehaviour
    {
        [SerializeField] private BodyZone zone = BodyZone.Torso;

        public BodyZone Zone => zone;
    }
}