using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Extensions.Returns;
using KnockBox.CardCounter.Services.State.Games;

namespace KnockBox.CardCounter.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Terminal state: the main deck has been exhausted.
    /// Sets <see cref="GamePhase.GameOver"/> and accepts no further commands.
    /// </summary>
    public sealed class GameOverState : ICardCounterGameState
    {
        public ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> OnEnter(CardCounterGameContext context)
        {
            context.State.SetPhase(GamePhase.GameOver);

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
            return null;
        }

        public Result OnExit(CardCounterGameContext context) => Result.Success;

        public ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> HandleCommand(CardCounterGameContext context, CardCounterCommand command) => null;
    }
}
