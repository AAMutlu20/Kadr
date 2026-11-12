using System;

namespace FSM
{
    /// <summary>
    /// Holds the current IState and swaps it. Deliberately knows nothing about enemies,
    /// so the same class can drive the regular zombies and, later, the bosses with their phases.
    ///
    /// Rule for state authors: ChangeState may be called from inside a state's own Tick. Anything that
    /// follows that call in the same Tick still runs, so always `return` straight after ChangeState.
    ///
    /// Changed fires after every switch with (previous, next). Previous is null for the
    /// very first state. Bosses use it to publish BossPhaseChanged, and it is handy for debug logging.
    /// </summary>
    public sealed class StateMachine
    {
        public IState Current { get; private set; }

        public event Action<IState, IState> Changed;

        public void ChangeState(IState next)
        {

            if (next == Current)
            {
                return;
            }

            var previous = Current;
            previous?.Exit();

            Current = next;
            Current.Enter();

            Changed?.Invoke(previous, next);
        }

        public void Tick(float deltaTime)
        {
            Current?.Tick(deltaTime);
        }
    }
}