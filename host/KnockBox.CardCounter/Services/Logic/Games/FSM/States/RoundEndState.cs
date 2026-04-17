using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;

namespace KnockBox.CardCounter.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Transient state entered when the current shoe is exhausted or when the game first
    /// starts (after buy-in). Deals action cards, then deals the next shoe.
    /// Transitions to <see cref="PlayerTurnState"/> if a shoe was dealt, or
    /// <see cref="GameOverState"/> if the main deck is now empty.
    /// </summary>
    public sealed class RoundEndState : ITimedCardCounterGameState
    {
        private DateTimeOffset _expirationTime;

        public ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> OnEnter(CardCounterGameContext context)
        {
            _expirationTime = DateTimeOffset.Now.AddMilliseconds(context.Config.RoundEndTimeoutMs);
            context.Logger.LogDebug("FSM → RoundEndState (shoe {n} exhausted).", context.State.ShoeIndex);

            var hasShoe = context.DealNextShoe();
            if (!hasShoe)
                return new GameOverState();

            context.State.IsNewShoe = true;
            context.DealActionCards();

            // Immediately transition if all players are under limit
            if (!context.GamePlayers.Values.Any(state => state.ActionHand.Count > context.Config.ActionHandLimit))
                return new PlayerTurnState();

            return null;
        }

        public Result OnExit(CardCounterGameContext context) => Result.Success;

        public ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> HandleCommand(CardCounterGameContext context, CardCounterCommand command)
        {
            if (command is not DiscardActionCardsCommand discardCommand) return null;
            var state = context.GetPlayer(discardCommand.PlayerId);
            if (state is null) return null;

            HandleDiscard(context, discardCommand);

            // Don't leave state if not all players have discarded extra cards
            if (context.GamePlayers.Values.Any(state => state.ActionHand.Count > context.Config.ActionHandLimit))
                return null;

            return new PlayerTurnState();
        }

        public ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> Tick(CardCounterGameContext context, DateTimeOffset now)
        {
            if (now < _expirationTime) return null;

            int actionHandLimit = context.Config.ActionHandLimit;

            // Discard last cards automatically
            foreach (var (id, state) in context.GamePlayers.Where(state => state.Value.ActionHand.Count > context.Config.ActionHandLimit))
            {
                int excessCards = state.ActionHand.Count - actionHandLimit;
                HandleDiscard(context, new DiscardActionCardsCommand(id, [.. Enumerable.Range(actionHandLimit, excessCards)]));
            }

            return new PlayerTurnState();
        }

        public ValueResult<TimeSpan> GetRemainingTime(CardCounterGameContext context, DateTimeOffset now) => _expirationTime - now;

        private static void HandleDiscard(CardCounterGameContext context, DiscardActionCardsCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null) return;

            if (player.ActionHand.Count <= context.Config.ActionHandLimit)
            {
                context.Logger.LogWarning("Discard: player [{id}] is not over the action hand limit.", cmd.PlayerId);
                return;
            }

            // Validate indices: must be distinct, in range, and discard enough to be at or under limit
            var indices = cmd.CardIndices;
            if (indices.Length == 0 || indices.Distinct().Count() != indices.Length
                || indices.Any(i => i < 0 || i >= player.ActionHand.Count))
            {
                context.Logger.LogWarning("Discard: invalid card indices from player [{id}].", cmd.PlayerId);
                return;
            }

            int afterDiscard = player.ActionHand.Count - indices.Length;
            if (afterDiscard > context.Config.ActionHandLimit)
            {
                context.Logger.LogWarning("Discard: player [{id}] must discard enough to reach the hand limit.", cmd.PlayerId);
                return;
            }

            // Remove in descending index order to preserve correctness
            foreach (var idx in indices.OrderByDescending(i => i))
                player.ActionHand.RemoveAt(idx);

            context.Logger.LogDebug(
                "Player [{id}] discarded {n} action cards.", cmd.PlayerId, indices.Length);
        }
    }
}
