using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Terminal display state that shows the final leaderboard and game-over screen.
    ///
    /// If returning from <see cref="CoinFlipState"/> with resolved final-standings flips,
    /// re-ranks the leaderboard using the flip results.
    ///
    /// Transition ownership:
    /// - <see cref="PlayAgainCommand"/> (host only) → <see cref="LobbyState"/>
    /// </summary>
    public sealed class FinalResultsDisplayState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.Results);
            context.Logger.LogInformation("FSM → FinalResultsDisplayState. Game complete.");

            // If returning from coin flip, re-rank the leaderboard.
            var resolvedFlips = context.State.PendingCoinFlipQueue
                .Where(f => f.IsResolved && f.Context == CoinFlipContext.FinalStandingsTie)
                .ToList();

            if (resolvedFlips.Count > 0)
            {
                context.Logger.LogInformation(
                    "Re-ranking leaderboard with {count} resolved final standings coin flips.",
                    resolvedFlips.Count);

                DrawnToDressScoringService.ApplyCoinFlipTiebreaks(
                    context.State.Leaderboard, context.State.PendingCoinFlipQueue);
            }
            else
            {
                // Set matchup_wins tiebreak method where applicable.
                DrawnToDressScoringService.SetMatchupWinsTiebreakMethod(context.State.Leaderboard);
            }

            return null;
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case PlayAgainCommand cmd:
                    if (cmd.PlayerId != context.State.Host.Id)
                    {
                        context.Logger.LogWarning(
                            "PlayAgain rejected: player [{id}] is not the host.", cmd.PlayerId);
                        return null;
                    }
                    context.Logger.LogInformation("Host [{id}] requested Play Again.", cmd.PlayerId);
                    return new LobbyState();

                default:
                    context.Logger.LogWarning(
                        "FinalResultsDisplayState: unrecognized command [{type}] from player [{id}].",
                        command.GetType().Name, command.PlayerId);
                    return null;
            }
        }
    }
}
