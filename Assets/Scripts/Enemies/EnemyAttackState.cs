using FSM;
using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// A melee attack: start the animation and land the hit either at Stats.attackHitDelay (timer) or, when
    /// Stats.hitFromAnimationEvent is ticked, from the clip's DealDamage Animation Event (EnemyAnimationEvents).
    /// After Stats.attackCooldown either swing again (player still in reach), chase again (player stepped away)
    /// or go idle (player is dead).
    /// The enemy stands still and turns to face the player while attacking.
    /// </summary>
    public class EnemyAttackState : IState
    {
        private readonly EnemyController _enemy;
        private float _timer;
        private bool _hitLanded;
        private float _lunged;

        public EnemyAttackState(EnemyController enemy)
        {
            _enemy = enemy;
        }

        public void Enter()
        {
            _enemy.Agent.isStopped = true;
            _enemy.Agent.velocity = Vector3.zero;
            StartSwing();
        }

        public void Tick(float deltaTime)
        {
            if (!_enemy.HasTarget)
            {
                _enemy.Fsm.ChangeState(_enemy.Idle);
                return;
            }

            _enemy.FaceTarget(deltaTime);
            _timer += deltaTime;
            Lunge(deltaTime);

            // With an Animation Event on the swing frame the clip decides when the hit lands, not this timer.
            if (!_enemy.Stats.hitFromAnimationEvent && !_hitLanded && _timer >= _enemy.Stats.attackHitDelay)
            {
                _hitLanded = true;
                _enemy.DealDamage();
            }

            if (_timer < _enemy.Stats.attackCooldown) return;

            if (_enemy.IsTargetInAttackReach())
            {
                StartSwing();
            }
            else
            {
                _enemy.Fsm.ChangeState(_enemy.Chase);
            }
        }

        public void Exit()
        {
            if (_enemy.Agent.enabled)
            {
                _enemy.Agent.isStopped = false;
            }
        }

        /// <summary>Steps forward during the swing (Stats.lungeDistance), so the hit lands where the player is now, not where they were.</summary>
        private void Lunge(float deltaTime)
        {
            var stats = _enemy.Stats;
            if (stats.lungeDistance <= 0f) return;

            var window = Mathf.Max(0.05f, stats.lungeSeconds > 0f ? stats.lungeSeconds : stats.attackHitDelay);
            if (_timer > window + deltaTime || _lunged >= stats.lungeDistance) return;

            var planned = Mathf.Min(stats.lungeDistance / window * deltaTime, stats.lungeDistance - _lunged);
            _enemy.LungeStep(planned);
            _lunged += planned; // counted even if the player is too close to move, so the timing stays the same
        }

        private void StartSwing()
        {
            _lunged = 0f;
            _timer = 0f;
            _hitLanded = false;
            _enemy.PlayAttackAnimation();
        }
    }
}