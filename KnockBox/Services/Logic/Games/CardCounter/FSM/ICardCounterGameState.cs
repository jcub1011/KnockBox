namespace KnockBox.Services.Logic.Games.CardCounter.FSM
{
    /// <summary>
    /// Contract for a single FSM node in the Card Counter game.
    /// All mutation must occur inside <c>state.Execute()</c> at the call site (the engine).
    /// The methods here are called from within that locked context.
    /// </summary>
    public interface ICardCounterGameState
    {
        /// <summary>
        /// Called once when the FSM transitions into this state.
        /// Use to set <see cref="CardCounterGameState.GamePhase"/>, configure timers, etc.
        /// </summary>
        void OnEnter(CardCounterGameContext context);

        /// <summary>
        /// Processes an incoming player command. Returns the next state to transition to,
        /// or <c>null</c> if the state should remain unchanged.
        /// </summary>
        ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command);

        /// <summary>
        /// Called periodically to handle time-based transitions (e.g., action-response timeouts).
        /// Returns the next state, or <c>null</c> to stay in the current state.
        /// </summary>
        ICardCounterGameState? Tick(CardCounterGameContext context, DateTimeOffset now);
    }
}
