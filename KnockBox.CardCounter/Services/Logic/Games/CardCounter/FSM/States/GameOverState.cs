using KnockBox.Services.State.Games.CardCounter;

namespace KnockBox.Services.Logic.Games.CardCounter.FSM.States
{
    /// <summary>
    /// Terminal state: the main deck has been exhausted.
    /// Sets <see cref="GamePhase.GameOver"/> and accepts no further commands.
    /// </summary>
    public sealed class GameOverState : ICardCounterGameState
    {
        public void OnEnter(CardCounterGameContext context)
        {
            context.State.GamePhase = GamePhase.GameOver;
            context.Logger.LogInformation("FSM → GameOverState. Game ended.");
        }

        public ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command) => null;
    }
}
