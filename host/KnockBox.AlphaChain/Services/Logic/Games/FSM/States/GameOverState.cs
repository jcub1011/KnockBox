using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Terminal state. Computes the final standings (rank by score) and stores them on
    /// <c>AlphaChainGameState.Results</c> for the results screen.
    /// </summary>
    public sealed class GameOverState : IGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        public ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;
            state.SetPhase(AlphaChainGamePhase.GameOver);

            var standings = state.GamePlayers.Values
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select((p, i) => new PlayerResult(p.UserId, p.DisplayName, p.Score, i + 1))
                .ToList();

            state.Results = new GameResults(standings);

            context.Logger.LogDebug("Alpha Chain FSM → GameOverState ({count} players ranked)", standings.Count);

            return null;
        }

        public Result OnExit(AlphaChainGameContext context) => Result.Success;

        public ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> HandleCommand(
            AlphaChainGameContext context, AlphaChainCommand command) => null;
    }
}
