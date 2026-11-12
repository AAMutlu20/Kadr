using FSM;

namespace Enemies
{
    /// <summary>
    /// Standing still, playing the Idle animation, waiting for the player. Checks for the player a few times
    /// a second instead of every frame, since the sight check is a raycast and a zombie does not need
    /// frame-perfect reactions.
    /// </summary>
    public class EnemyIdleState : IState
    {
        private const float SenseInterval = 0.2f;

        private readonly EnemyController _enemy;
        private float _senseTimer;

        public EnemyIdleState(EnemyController enemy)
        {
            _enemy = enemy;
        }

        public void Enter()
        {
            _enemy.Agent.ResetPath();
            _enemy.Agent.isStopped = true;
            _senseTimer = 0f;
        }

        public void Tick(float deltaTime)
        {
            _senseTimer -= deltaTime;
            if (_senseTimer > 0f) return;

            _senseTimer = SenseInterval;

            if (_enemy.CanNoticeTarget())
            {
                _enemy.BeginChase();
            }
        }

        public void Exit() { }
    }
}