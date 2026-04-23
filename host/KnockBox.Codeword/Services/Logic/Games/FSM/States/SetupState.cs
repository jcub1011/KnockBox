using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Codeword.Services.State.Games;

namespace KnockBox.Codeword.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Timed setup phase (5s default). Increments the elimination cycle, assigns roles,
    /// selects the word pair, randomizes clue order, and auto-advances to
    /// <see cref="CluePhaseState"/> on timeout.
    /// </summary>
    public sealed class SetupState : ITimedCodewordGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> OnEnter(CodewordGameContext context)
        {
            context.State.CurrentEliminationCycle++;
            context.AssignRoles();

            // Randomize clue order only on the first cycle of a game.
            // Subsequent cycles preserve TurnOrder so that CurrentCluePlayerIndex
            // carries over (rotating start player).
            if (context.State.CurrentEliminationCycle == 1)
            {
                var turnOrder = context.State.TurnManager.TurnOrder;
                for (int i = turnOrder.Count - 1; i > 0; i--)
                {
                    int j = context.Rng.GetRandomInt(0, i + 1);
                    (turnOrder[i], turnOrder[j]) = (turnOrder[j], turnOrder[i]);
                }
            }

            context.State.SetPhase(CodewordGamePhase.Setup);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.SetupPhaseTimeoutMs);

            context.Logger.LogDebug(
                "FSM → SetupState (cycle {cycle})", context.State.CurrentEliminationCycle);

            return null;
        }

        public Result OnExit(CodewordGameContext context) => Result.Success;

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> HandleCommand(
            CodewordGameContext context, CodewordCommand command) => null;

        public ValueResult<IGameState<CodewordGameContext, CodewordCommand>?> Tick(
            CodewordGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;
            return new CluePhaseState();
        }

        public ValueResult<TimeSpan> GetRemainingTime(CodewordGameContext context, DateTimeOffset now)
            => _expiresAt - now;
    }
}
