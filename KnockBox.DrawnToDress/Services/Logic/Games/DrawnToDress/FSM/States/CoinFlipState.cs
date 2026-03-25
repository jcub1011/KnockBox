using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Resolves a tied voting matchup via a random coin flip and immediately chains to
    /// <see cref="VotingRoundResultsState"/>.
    ///
    /// The outcome is recorded on <see cref="DrawnToDressGameState.PendingCoinFlipMatchupId"/>
    /// (cleared on exit) so the UI can animate the flip.
    ///
    /// Transition ownership:
    /// - <see cref="OnEnter"/> performs the flip and chains to
    ///   <see cref="VotingRoundResultsState"/> without waiting for player input.
    /// </summary>
    public sealed class CoinFlipState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.CoinFlip);
            context.Logger.LogInformation(
                "FSM → CoinFlipState. Flipping coin for matchup [{matchupId}].",
                context.State.PendingCoinFlipMatchupId);

            // TODO: Record the CoinFlipResult in state for the UI to display.
            // Placeholder: just log and proceed.
            bool isHeads = Random.Shared.Next(2) == 0;
            context.Logger.LogInformation(
                "Coin flip result: {result}.", isHeads ? "Heads" : "Tails");

            return new VotingRoundResultsState();
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.PendingCoinFlipMatchupId = null;
            return Result.Success;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command) => null;
    }
}
