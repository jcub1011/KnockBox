using KnockBox.Services.State.Games.CardCounter;

namespace KnockBox.Services.Logic.Games.CardCounter.FSM.States
{
    /// <summary>
    /// Waits for every player to commit their buy-in sign (positive or negative).
    /// Transitions to <see cref="RoundEndState"/> (which deals action cards and the first shoe)
    /// once all players have set their buy-in.
    /// </summary>
    public sealed class BuyInState : ICardCounterGameState
    {
        private DateTimeOffset _expirationTime;

        public void OnEnter(CardCounterGameContext context)
        {
            _expirationTime = DateTimeOffset.Now.AddMilliseconds(context.Config.BuyInTimeoutMs);
            context.State.GamePhase = GamePhase.BuyIn;
            context.Logger.LogInformation("FSM → BuyInState");
        }

        public ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command)
        {
            if (command is not SetBuyInCommand cmd)
                return null;

            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning("SetBuyIn: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            if (player.HasSetBuyIn)
            {
                context.Logger.LogWarning("SetBuyIn: player [{id}] already set their buy-in.", cmd.PlayerId);
                return null;
            }

            double balance = player.BuyInRoll * 8;
            player.Balance = cmd.IsNegative ? -balance : balance;
            player.HasSetBuyIn = true;

            context.Logger.LogInformation(
                "Player [{id}] set buy-in: {sign}{value}.",
                cmd.PlayerId, cmd.IsNegative ? "-" : "+", balance);

            bool allSet = context.GamePlayers.Values.All(p => p.HasSetBuyIn);
            return allSet ? new RoundEndState() : null;
        }

        public ICardCounterGameState? Tick(CardCounterGameContext context, DateTimeOffset now)
        {
            if (now < _expirationTime) return null;

            // Default unselected players to positive buy-in
            foreach (var player in context.GamePlayers.Values.Where(state => !state.HasSetBuyIn))
            {
                double balance = player.BuyInRoll * 8;
                player.Balance = balance;
                player.HasSetBuyIn = true;
            }

            return new RoundEndState();
        }
    }
}
