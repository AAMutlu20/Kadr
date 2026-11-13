using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// Spawns a particle prefab (blood, for the zombies) where a bullet hits this object.
    /// Goes on the enemy's root, next to Health. The prefab is rotated so its local +Z points out of the
    /// body, which makes a cone-shaped particle system spray away from the wound.
    ///
    /// Every spawned effect destroys itself after <see cref="lifetime"/> seconds. The shotgun hits with
    /// up to 8 pellets per shot, so keep the prefab's particle count small. If it ever gets heavy,
    /// this is the place to add pooling.
    /// </summary>
    public class HitEffect : MonoBehaviour, IHitReceiver
    {
        [Tooltip("Particle prefab to spawn on every hit. Its local +Z should be the spray direction.")]
        [SerializeField] private GameObject effectPrefab;

        [Tooltip("Seconds before the spawned effect is destroyed. Longer than the particles' own lifetime.")]
        [SerializeField] private float lifetime = 3f;

        public void OnHit(Vector3 point, Vector3 normal)
        {
            if (!effectPrefab) return;

            var direction = normal.sqrMagnitude > 0.001f ? normal : Vector3.up;
            var effect = Instantiate(effectPrefab, point, Quaternion.LookRotation(direction));
            Destroy(effect, lifetime);
        }
    }
}