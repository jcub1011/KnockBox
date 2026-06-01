using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// The core turn loop. Players take turns via <see cref="AdvanceTurnCommand"/>;
    /// when the turn order wraps, the canonical era/round rule decides whether the
    /// game ends. The shot-clock timer is set here but has no consequence until M2.
    /// </summary>
    public sealed class RoundState : IGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        public ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;
            state.SetPhase(AlphaChainGamePhase.Round);
            state.PhaseEndTime = DateTimeOffset.UtcNow.AddSeconds(state.Settings.ShotClockSeconds);

            context.Logger.LogDebug(
                "Alpha Chain FSM → RoundState (era {era}, round {round}, active {player})",
                state.CurrentEra, state.CurrentRound, state.TurnManager.CurrentPlayer);

            return null;
        }

        public Result OnExit(AlphaChainGameContext context) => Result.Success;

        public ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> HandleCommand(
            AlphaChainGameContext context, AlphaChainCommand command)
        {
            return command switch
            {
                AdvanceTurnCommand cmd => HandleAdvanceTurn(context, cmd),
                _ => (ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?>)null!
            };
        }

        private static ValueResult<IGameState<AlphaChainGameContext, AlphaChainCommand>?> HandleAdvanceTurn(
            AlphaChainGameContext context, AdvanceTurnCommand cmd)
        {
            var state = context.State;
            var turnManager = state.TurnManager;

            if (cmd.ActorUserId != turnManager.CurrentPlayer)
                return new ResultError("It is not your turn.",
                    $"Player [{cmd.ActorUserId}] tried to advance but the active player is [{turnManager.CurrentPlayer}].");

            bool wrapped = turnManager.NextTurn();
            if (!wrapped)
                return null;

            // The turn order just wrapped — a round completed. Evaluate the canonical
            // end condition against the round that just finished.
            int completedRound = state.CurrentRound;
            int lastScheduledRound = state.Settings.EraInterval * state.Settings.EraCount;

            // Rule 1: game over on the final scheduled round (no Intermission ever follows it).
            if (completedRound == lastScheduledRound)
                return new GameOverState();

            // Rule 2 (era boundary → Intermission) is a no-op in M1; lands in M4.
            // Rule 3: continue with the next round.
            state.CurrentRound++;

            return null;
        }
    }
}
