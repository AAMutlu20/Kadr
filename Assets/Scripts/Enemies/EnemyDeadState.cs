using FSM;
using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// Final state: nothing leaves it. Plays the death animation, switches off everything that made the
    /// enemy interact with the world (agent, colliders, so bullets pass through the corpse and it blocks
    /// nothing) and destroys the object after Stats.corpseLifetime seconds.
    /// EnemyKilledEvent is published by EnemyController.OnDied, before this state starts.
    /// </summary>
    public class EnemyDeadState : IState
    {
        private readonly EnemyController _enemy;

        public EnemyDeadState(EnemyController enemy)
        {
            _enemy = enemy;
        }

        public void Enter()
        {
            _enemy.PlayDeathAnimation();

            _enemy.Agent.enabled = false;

            foreach (var collider in _enemy.GetComponentsInChildren<Collider>())
            {
                collider.enabled = false;
            }

            Object.Destroy(_enemy.gameObject, _enemy.Stats.corpseLifetime);
        }

        public void Tick(float deltaTime) { }

        public void Exit() { }
    }
}