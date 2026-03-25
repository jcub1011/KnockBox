using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Terminal state that displays the final leaderboard and game-over screen.
    ///
    /// No further gameplay transitions are possible from this state other than abandoning
    /// (e.g. to clean up resources on the server).
    ///
    /// Transition ownership:
    /// - <see cref="AbandonGameCommand"/> → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class FinalResultsState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.Results);
            context.Logger.LogInformation("FSM → FinalResultsState. Game complete.");

            // TODO: Compute and write leaderboard entries to state in a later issue.
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            if (command is AbandonGameCommand)
                return new AbandonedState();

            return null;
        }
    }
}
