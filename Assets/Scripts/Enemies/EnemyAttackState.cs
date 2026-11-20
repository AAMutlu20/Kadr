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

        private void StartSwing()
        {
            _timer = 0f;
            _hitLanded = false;
            _enemy.PlayAttackAnimation();
        }
    }
}