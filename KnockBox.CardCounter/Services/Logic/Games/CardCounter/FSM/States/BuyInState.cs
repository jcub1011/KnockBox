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
        public void OnEnter(CardCounterGameContext context)
        {
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

            long balance = player.BuyInRoll * 8;
            player.Balance = cmd.IsNegative ? -balance : balance;
            player.HasSetBuyIn = true;

            context.Logger.LogInformation(
                "Player [{id}] set buy-in: {sign}{value}.",
                cmd.PlayerId, cmd.IsNegative ? "-" : "+", balance);

            bool allSet = context.GamePlayers.Values.All(p => p.HasSetBuyIn);
            return allSet ? new RoundEndState() : null;
        }

        public ICardCounterGameState? Tick(CardCounterGameContext context, DateTimeOffset now) => null;
    }
}
