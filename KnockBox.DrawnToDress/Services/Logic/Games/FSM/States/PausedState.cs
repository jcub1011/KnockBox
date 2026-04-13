using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Extensions.Returns;
using KnockBox.DrawnToDress.Services.State.Games;

namespace KnockBox.DrawnToDress.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Suspends the game while preserving the state that was active at the time of the
    /// pause. Resuming restores the game to that saved state.
    ///
    /// Transition ownership:
    /// - <see cref="ResumeGameCommand"/> (host only) → returns to <see cref="_resumeState"/>
    /// </summary>
    public sealed class PausedState(IDrawnToDressGameState resumeState) : IDrawnToDressGameState
    {
        /// <summary>The state the FSM will return to when the game is resumed.</summary>
        private readonly IDrawnToDressGameState _resumeState = resumeState;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.Paused);
            context.Logger.LogInformation(
                "FSM → PausedState (will resume to [{state}]).",
                _resumeState.GetType().Name);
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case ResumeGameCommand cmd:
                    if (cmd.PlayerId != context.State.Host.Id)
                    {
                        context.Logger.LogWarning(
                            "ResumeGame rejected: player [{id}] is not the host.", cmd.PlayerId);
                        return null;
                    }
                    context.Logger.LogInformation(
                        "Host resumed game. Returning to [{state}].",
                        _resumeState.GetType().Name);
                    return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(_resumeState);


                default:
                    return null;
            }
        }
    }
}
