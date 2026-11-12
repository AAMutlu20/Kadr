namespace FSM
{
    /// <summary>
    /// One state of a finite state machine (FSM pattern, the module's second pattern).
    ///
    /// Pattern roles in Kadr:
    /// - Context:  the object being driven (EnemyController), which owns a <see cref="StateMachine"/>.
    /// - State:    this interface. One small class per behaviour (idle, chase, attack, dead).
    /// - Machine:  <see cref="StateMachine"/>, which only knows "the current state" and how to swap it.
    ///
    /// A state decides for itself when to hand over to another one, by calling ChangeState on the
    /// machine. The context never contains "if idle ... else if chasing ..." branches.
    /// </summary>
    public interface IState
    {
        /// <summary>Runs once when the machine switches to this state. Set things up here.</summary>
        void Enter();

        /// <summary>Runs every frame while this state is current.</summary>
        void Tick(float deltaTime);

        /// <summary>Runs once when the machine switches away. Undo whatever Enter changed.</summary>
        void Exit();
    }
}