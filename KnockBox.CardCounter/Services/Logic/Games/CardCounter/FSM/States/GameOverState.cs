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

            foreach (var player in context.GamePlayers.Values)
            {
                if (player.Pot.Count == 0)
                    continue;

                double potValue = player.PotValue;
                player.Pot.Clear();
                player.Balance = player.Balance < 0
                    ? player.Balance - potValue
                    : player.Balance + potValue;
            }

            context.Logger.LogInformation("FSM → GameOverState. Game ended.");
        }

        public ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command) => null;
    }
}
