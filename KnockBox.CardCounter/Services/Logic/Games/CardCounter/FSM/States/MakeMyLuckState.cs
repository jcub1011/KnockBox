namespace KnockBox.Services.Logic.Games.CardCounter.FSM.States
{
    /// <summary>
    /// Waiting for the player who played Make My Luck to submit their chosen card order.
    /// The player sees the top-3 shoe cards in <see cref="PlayerState.PrivateReveal"/> and
    /// must select an order via <see cref="SubmitReorderCommand"/>.
    /// </summary>
    public sealed class MakeMyLuckState : ITimedCardCounterGameState
    {
        private readonly string _playerId;
        private DateTimeOffset _expiresAt;

        public MakeMyLuckState(string playerId)
        {
            _playerId = playerId;
        }

        public void OnEnter(CardCounterGameContext context)
        {
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.Config.MakeMyLuckTimeoutMs);
            context.Logger.LogInformation(
                "FSM → MakeMyLuckState for [{id}]. Expires {exp}.", _playerId, _expiresAt);
        }

        public ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command)
        {
            if (command is not SubmitReorderCommand cmd || cmd.PlayerId != _playerId)
                return null;

            var player = context.GetPlayer(_playerId);
            if (player?.PrivateReveal is null)
            {
                context.Logger.LogWarning("MakeMyLuck: no private reveal for [{id}].", _playerId);
                return ResolveDefault(context, player);
            }

            var reveal = player.PrivateReveal;
            int revealCount = reveal.Count;

            if (cmd.ReorderedIndices.Length != revealCount ||
                cmd.ReorderedIndices.Distinct().Count() != revealCount ||
                cmd.ReorderedIndices.Any(i => i < 0 || i >= revealCount))
            {
                context.Logger.LogWarning("MakeMyLuck: invalid reorder indices from [{id}].", _playerId);
                return null;
            }

            // Build the reordered cards
            var reordered = cmd.ReorderedIndices.Select(i => reveal[i]).ToList();

            // Pop the top cards off the shoe and push back in the new order (last pushed = new top)
            for (int i = 0; i < revealCount; i++)
                context.CurrentShoe.Pop();

            // Push in reverse so that reordered[0] ends up on top
            for (int i = revealCount - 1; i >= 0; i--)
                context.CurrentShoe.Push(reordered[i]);

            player.PrivateReveal = null;
            context.Logger.LogInformation("MakeMyLuck: player [{id}] reordered top {n} cards.", _playerId, revealCount);
            return new PlayerTurnState();
        }

        public ICardCounterGameState? Tick(CardCounterGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            context.Logger.LogInformation("MakeMyLuck: timeout for [{id}]; keeping original order.", _playerId);
            var player = context.GetPlayer(_playerId);
            return ResolveDefault(context, player);
        }

        public TimeSpan GetRemainingTime(CardCounterGameContext context, DateTimeOffset now) => _expiresAt - now;

        private static PlayerTurnState ResolveDefault(CardCounterGameContext context, State.Games.CardCounter.Data.PlayerState? player)
        {
            if (player is not null) player.PrivateReveal = null;
            return new PlayerTurnState();
        }
    }
}
