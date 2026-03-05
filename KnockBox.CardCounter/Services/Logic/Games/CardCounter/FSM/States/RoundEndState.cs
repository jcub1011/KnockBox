using KnockBox.Services.State.Games.CardCounter;

namespace KnockBox.Services.Logic.Games.CardCounter.FSM.States
{
    /// <summary>
    /// Transient state entered when the current shoe is exhausted or when the game first
    /// starts (after buy-in). Deals action cards, then deals the next shoe.
    /// Transitions to <see cref="PlayerTurnState"/> if a shoe was dealt, or
    /// <see cref="GameOverState"/> if the main deck is now empty.
    /// </summary>
    public sealed class RoundEndState : ICardCounterGameState
    {
        public void OnEnter(CardCounterGameContext context)
        {
            context.Logger.LogInformation("FSM → RoundEndState (shoe {n} exhausted).", context.State.ShoeIndex);
            context.DealActionCards();

            bool hasShoe = context.DealNextShoe();

            if (hasShoe)
                context.State.IsNewShoe = true;

            // Immediately transition — this state is fully resolved on enter.
            var next = hasShoe ? (ICardCounterGameState)new PlayerTurnState() : new GameOverState();
            context.CurrentFsmState = next;
            next.OnEnter(context);
        }

        public ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command) => null;

        public ICardCounterGameState? Tick(CardCounterGameContext context, DateTimeOffset now) => null;
    }
}
