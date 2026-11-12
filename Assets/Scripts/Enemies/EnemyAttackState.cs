using FSM;
using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// A melee attack on a timer, so it does not depend on animation events:
    /// start the animation, land the hit at Stats.attackHitDelay, and after Stats.attackCooldown either
    /// swing again (player still in reach), chase again (player stepped away) or go idle (player is dead).
    /// The enemy stands still and turns to face the player while attacking.
    /// </summary>
    public class EnemyAttackState : IState
    {
        private readonly EnemyController _enemy;
        private float _timer;
        private bool _hitLanded;

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

            if (!_hitLanded && _timer >= _enemy.Stats.attackHitDelay)
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

        private void StartSwing()
        {
            _timer = 0f;
            _hitLanded = false;
            _enemy.PlayAttackAnimation();
        }
    }
}