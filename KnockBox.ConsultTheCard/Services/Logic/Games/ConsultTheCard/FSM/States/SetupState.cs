using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.ConsultTheCard;

namespace KnockBox.Services.Logic.Games.ConsultTheCard.FSM.States
{
    /// <summary>
    /// Timed setup phase (5s default). Increments the elimination cycle, assigns roles,
    /// selects the word pair, randomizes clue order, and auto-advances to
    /// <see cref="CluePhaseState"/> on timeout.
    /// </summary>
    public sealed class SetupState : ITimedConsultTheCardGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> OnEnter(ConsultTheCardGameContext context)
        {
            context.State.CurrentEliminationCycle++;
            context.AssignRoles();

            // Randomize clue order only on the first cycle of a game.
            // Subsequent cycles preserve TurnOrder so that CurrentCluePlayerIndex
            // carries over (rotating start player).
            if (context.State.CurrentEliminationCycle == 1)
            {
                var turnOrder = context.State.TurnOrder;
                for (int i = turnOrder.Count - 1; i > 0; i--)
                {
                    int j = context.Rng.GetRandomInt(0, i + 1);
                    (turnOrder[i], turnOrder[j]) = (turnOrder[j], turnOrder[i]);
                }
            }

            context.State.GamePhase = ConsultTheCardGamePhase.Setup;
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.SetupPhaseTimeoutMs);

            context.Logger.LogInformation(
                "FSM → SetupState (cycle {cycle})", context.State.CurrentEliminationCycle);

            return null;
        }

        public Result OnExit(ConsultTheCardGameContext context) => Result.Success;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> HandleCommand(
            ConsultTheCardGameContext context, ConsultTheCardCommand command) => null;

        public ValueResult<IGameState<ConsultTheCardGameContext, ConsultTheCardCommand>?> Tick(
            ConsultTheCardGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;
            return new CluePhaseState();
        }

        public ValueResult<TimeSpan> GetRemainingTime(ConsultTheCardGameContext context, DateTimeOffset now)
            => _expiresAt - now;
    }
}
