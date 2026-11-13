using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// Anything that wants to react visually to being hit by a bullet, at the exact spot it was hit.
    /// </summary>
    public interface IHitReceiver
    {
        /// <param name="point">World position of the hit.</param>
        /// <param name="normal">Surface normal at the hit, pointing out of the target toward the shooter.</param>
        void OnHit(Vector3 point, Vector3 normal);
    }
}