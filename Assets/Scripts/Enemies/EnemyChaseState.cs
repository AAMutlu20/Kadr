using FSM;
using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// Walking after the player. While the player is visible the destination follows them; once they break
    /// line of sight the enemy keeps walking to the last place it saw them, and gives up after
    /// Stats.loseSightTime seconds (or if the player gets further than Stats.loseRange).
    /// If the enemy has an opening sprint (Stats.sprintSpeed), it runs for Stats.sprintDuration seconds after
    /// noticing the player and then drops to its walking speed. The sprint only starts when
    /// EnemyController.BeginChase asked for it, not when coming back from an attack.
    /// Re-pathing is throttled: NavMesh path requests are not free and a slow zombie does not need more.
    /// </summary>
    public class EnemyChaseState : IState
    {
        private const float RepathInterval = 0.25f;

        private readonly EnemyController _enemy;
        private float _sprintTimer;
        private float _repathTimer;
        private float _lostSightTimer;
        private Vector3 _lastKnownPosition;

        public EnemyChaseState(EnemyController enemy)
        {
            _enemy = enemy;
        }

        /// <summary>Set by EnemyController.BeginChase, consumed (and cleared) by the next Enter.</summary>
        public bool SprintRequested { get; set; }

        public void Enter()
        {
            _enemy.Agent.isStopped = false;

            if (SprintRequested)
            {
                _sprintTimer = _enemy.Stats.sprintDuration;
                _enemy.Agent.speed = _enemy.Stats.sprintSpeed;
                SprintRequested = false;
            }
            else
            {
                _sprintTimer = 0f;
                _enemy.Agent.speed = _enemy.Stats.moveSpeed;
            }

            _repathTimer = 0f;
            _lostSightTimer = 0f;

            if (_enemy.HasTarget)
            {
                _lastKnownPosition = _enemy.Target.position;
            }
        }

        public void Tick(float deltaTime)
        {
            if (!_enemy.HasTarget)
            {
                _enemy.Fsm.ChangeState(_enemy.Idle);
                return;
            }

            if (_enemy.IsTargetInAttackReach())
            {
                _enemy.Fsm.ChangeState(_enemy.Attack);
                return;
            }

            if (_sprintTimer > 0f)
            {
                _sprintTimer -= deltaTime;
                if (_sprintTimer <= 0f)
                {
                    _enemy.Agent.speed = _enemy.Stats.moveSpeed;
                }
            }

            _repathTimer -= deltaTime;
            if (_repathTimer > 0f) return;

            _repathTimer = RepathInterval;

            if (_enemy.CanSeeTarget())
            {
                _lostSightTimer = 0f;
                _lastKnownPosition = _enemy.Target.position;
            }
            else
            {
                _lostSightTimer += RepathInterval;
            }

            if (_lostSightTimer >= _enemy.Stats.loseSightTime || _enemy.DistanceToTarget > _enemy.Stats.loseRange)
            {
                _enemy.Fsm.ChangeState(_enemy.Idle);
                return;
            }

            _enemy.Agent.SetDestination(_lastKnownPosition);
        }

        public void Exit()
        {
            // Whatever state comes next starts at walking speed, never at a leftover sprint speed.
            if (_enemy.Agent.enabled)
            {
                _enemy.Agent.speed = _enemy.Stats.moveSpeed;
            }
        }
    }
}