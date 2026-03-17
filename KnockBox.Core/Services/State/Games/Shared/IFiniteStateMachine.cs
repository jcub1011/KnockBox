using KnockBox.Extensions.Events;
using KnockBox.Extensions.Returns;

namespace KnockBox.Core.Services.State.Games.Shared
{
    public readonly record struct StateChangeArgs<TContext, TCommand>(
        IGameState<TContext, TCommand>? PreviousState, IGameState<TContext, TCommand>? NewState);

    /// <summary>
    /// The contract for a Finite State Machine.
    /// </summary>
    /// <typeparam name="TContext"></typeparam>
    /// <typeparam name="TCommand"></typeparam>
    public interface IFininteStateMachine<TContext, TCommand>
    {
        /// <summary>
        /// Forces a transition to the provided state.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        Result TransitionTo(TContext context, IGameState<TContext, TCommand> state);

        /// <summary>
        /// Passes the command to the current state.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="command"></param>
        /// <returns>The state that was transitioned to or null if the state was unchanged.</returns>
        ValueResult<IGameState<TContext, TCommand>?> HandleCommand(TContext context, TCommand command);

        /// <summary>
        /// Ticks the current state.
        /// </summary>
        /// <param name="context"></param>
        /// <returns>The state that was transitioned to or null if the state was unchanged.</returns>
        ValueResult<IGameState<TContext, TCommand>?> Tick(TContext context, DateTimeOffset now);

        /// <summary>
        /// The event manager responsible for handling state change events.
        /// </summary>
        IThreadSafeEventManager<StateChangeArgs<TContext, TCommand>> StateChangedManager { get; }
    }

    /// <summary>
    /// The contract for a single FSM node.
    /// </summary>
    /// <typeparam name="TContext"></typeparam>
    /// <typeparam name="TCommand"></typeparam>
    public interface IGameState<TContext, TCommand>
    {
        /// <summary>
        /// Called once when the FSM transitions into this state.
        /// </summary>
        /// <param name="context"></param>
        /// <returns>The state to transition to or null if the state should remain unchanged.</returns>
        ValueResult<IGameState<TContext, TCommand>?> OnEnter(TContext context);

        /// <summary>
        /// Called once when the FSM exits this phase.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        Result OnExit(TContext context);

        /// <summary>
        /// Processes an incoming command. 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="command"></param>
        /// <returns>The state to transition to or null if the state should remain unchanged.</returns>
        ValueResult<IGameState<TContext, TCommand>?> HandleCommand(TContext context, TCommand command);
    }

    /// <summary>
    /// The contract for a single FSM machine node.
    /// </summary>
    /// <typeparam name="TContext"></typeparam>
    /// <typeparam name="TCommand"></typeparam>
    public interface ITimedGameState<TContext, TCommand> : IGameState<TContext, TCommand>
    {
        /// <summary>
        /// Gets the time remaining before a time-based state change occurs.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="now"></param>
        /// <returns></returns>
        ValueResult<TimeSpan> GetRemainingTime(TContext context, DateTimeOffset now);

        /// <summary>
        /// Called periodically to handle time-based transitions.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="now"></param>
        /// <returns>The state to transition to or null if the state should remain unchanged.</returns>
        ValueResult<IGameState<TContext, TCommand>?> Tick(TContext context, DateTimeOffset now);
    }
}
