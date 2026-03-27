using KnockBox.Extensions.Events;
using KnockBox.Extensions.Returns;

namespace KnockBox.Core.Services.State.Games.Shared
{
    /// <summary>
    /// The default implementation of a finite state machine.
    /// </summary>
    /// <typeparam name="TContext"></typeparam>
    /// <typeparam name="TCommand"></typeparam>
    /// <param name="logger"></param>
    public class FiniteStateMachine<TContext, TCommand>(ILogger? logger = null) 
        : IFininteStateMachine<TContext, TCommand>
    {
        public IThreadSafeEventManager<StateChangeArgs<TContext, TCommand>> StateChangedManager { get; } 
            = new ThreadSafeEventManager<StateChangeArgs<TContext, TCommand>>(logger);

        public IGameState<TContext, TCommand>? CurrentState { get; protected set; } = null;

        public ValueResult<IGameState<TContext, TCommand>?> HandleCommand(TContext context, TCommand command)
        {
            if (CurrentState is null)
                return new ResultError("Error handling command.", "Unable to handle state as CurrentState is null.");

            var result = CurrentState.HandleCommand(context, command);
            if (!result.IsSuccess) return result.Error.Error;

            var nextState = result.Value;
            if (nextState is null)
                return null;

            var transitionResult = TransitionTo(context, nextState);
            if (transitionResult.TryGetFailure(out var error)) return error;
            return ValueResult<IGameState<TContext, TCommand>?>.FromValue(CurrentState);
        }

        public ValueResult<IGameState<TContext, TCommand>?> Tick(TContext context, DateTimeOffset now)
        {
            if (CurrentState is null)
                return new ResultError("Error handling tick.", "Unable to handle tick as CurrentState is null.");

            if (CurrentState is not ITimedGameState<TContext, TCommand> timedState)
                return null;

            var result = timedState.Tick(context, now);
            if (!result.IsSuccess) return result.Error.Error;

            var nextState = result.Value;
            if (nextState is null)
                return null;

            var transitionResult = TransitionTo(context, nextState);
            if (transitionResult.TryGetFailure(out var error)) return error;
            return ValueResult<IGameState<TContext, TCommand>?>.FromValue(CurrentState);
        }

        public Result TransitionTo(TContext context, IGameState<TContext, TCommand> state)
        {
            var previousState = CurrentState;
            CurrentState = state;

            if (previousState is not null)
            {
                if (previousState.OnExit(context).TryGetFailure(out var error))
                {
                    // Rollback state change
                    CurrentState = previousState;
                    return error;
                }
            }

            while (true)
            {
                StateChangedManager.Notify(new(previousState, state));

                if (state is null)
                    return Result.Success;

                var enterResult = state.OnEnter(context);
                if (enterResult.TryGetFailure(out var enterError))
                    return enterError;

                var chainedState = enterResult.Value;
                if (chainedState is null)
                    return Result.Success;

                // OnEnter requested a chained transition — exit the current state and enter the next
                if (state.OnExit(context).TryGetFailure(out var exitError))
                    return exitError;

                previousState = state;
                state = chainedState;
                CurrentState = state;
            }
        }
    }
}
