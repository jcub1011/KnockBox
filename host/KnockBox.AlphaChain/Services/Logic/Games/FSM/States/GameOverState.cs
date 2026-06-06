using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Terminal state. Builds the final standings and stores them on
    /// <c>AlphaChainGameState.Results</c> for the results screen. Active players rank first by
    /// score (descending); eliminated players rank last in reverse-elimination order (lasting
    /// longer beats being knocked out early). Ties break by earliest turn-order index, which is
    /// deterministic. The rank-1 player is the winner — in Survival's last-player-standing finish
    /// the lone survivor is the only active player and so wins regardless of score.
    /// </summary>
    public sealed class GameOverState : IGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        public ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;
            state.SetPhase(AlphaChainGamePhase.GameOver);

            // Words played per player, from the chronological submission history.
            var wordsByPlayer = state.SubmissionHistory
                .GroupBy(p => p.UserId)
                .ToDictionary(g => g.Key, g => g.Count());

            int TurnIndex(Guid userId)
            {
                int idx = state.TurnManager.TurnOrder.IndexOf(userId);
                return idx < 0 ? int.MaxValue : idx;
            }

            // Active players: best score first. Eliminated players: behind everyone, ordered so the
            // last one eliminated (highest EliminationOrder) ranks above those out earlier.
            var active = state.GamePlayers.Values
                .Where(p => !p.IsEliminated)
                .OrderByDescending(p => p.Score)
                .ThenBy(p => TurnIndex(p.UserId));

            var eliminated = state.GamePlayers.Values
                .Where(p => p.IsEliminated)
                .OrderByDescending(p => p.EliminationOrder ?? 0)
                .ThenBy(p => TurnIndex(p.UserId));

            var rankings = active.Concat(eliminated)
                .Select(p => new PlayerResult(
                    p.UserId,
                    p.DisplayName,
                    p.Score,
                    p.IsEliminated,
                    wordsByPlayer.TryGetValue(p.UserId, out var n) ? n : 0))
                .ToList();

            Guid winner = rankings.Count > 0 ? rankings[0].UserId : Guid.Empty;
            var duration = DateTimeOffset.UtcNow - state.StartedAt;

            state.Results = new GameResults(rankings, winner, state.SubmissionHistory.Count, duration);

            context.Logger.LogDebug(
                "Alpha Chain FSM → GameOverState ({count} players ranked, winner [{winner}], {words} words)",
                rankings.Count, winner, state.SubmissionHistory.Count);

            return null;
        }

        public Result OnExit(AlphaChainGameContext context) => Result.Success;

        public ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> HandleCommand(
            AlphaChainGameContext context, AlphaChainCommand command) => null;
    }
}
