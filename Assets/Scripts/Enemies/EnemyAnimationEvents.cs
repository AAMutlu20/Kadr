using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// An Animation Event can only call methods on the GameObject that holds the Animator, so this forwards
    /// "DealDamage" up to the EnemyController. In the clip's event, pick the function DealDamage.
    /// </summary>
    public class EnemyAnimationEvents : MonoBehaviour
    {
        private EnemyController _enemy;

        private void Awake()
        {
            _enemy = GetComponentInParent<EnemyController>();
        }

        /// <summary>Called by the Animation Event on the swing frame.</summary>
        public void DealDamage()
        {
            if (!_enemy || _enemy.IsDead || _enemy.Stunned) return;

            _enemy.DealDamage();
        }
    }
}