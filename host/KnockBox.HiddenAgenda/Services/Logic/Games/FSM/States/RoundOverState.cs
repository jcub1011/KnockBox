using System;
using System.Linq;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.HiddenAgenda.Services.State.Games;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States
{
    public sealed class RoundOverState : IHiddenAgendaGameState
    {
        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(HiddenAgendaGameContext context)
        {
            // Accumulate round scores into cumulative scores
            foreach (var player in context.GamePlayers.Values)
            {
                player.CumulativeScore += player.RoundScore;
            }

            context.State.SetPhase(GamePhase.RoundOver);
            return null;
        }

        public Result OnExit(HiddenAgendaGameContext context) => Result.Success;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleCommand(
            HiddenAgendaGameContext context, HiddenAgendaCommand command)
        {
            if (command is not StartNextRoundCommand cmd) return null;

            // Host-only
            if (cmd.PlayerId != context.State.Host.Id)
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Only the host can start the next round.");

            // Check if match is over
            if (context.State.CurrentRound >= context.State.Config.TotalRounds)
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new MatchOverState());

            // Reset for new round and start
            context.ResetForNewRound();
            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new RoundSetupState());
        }
    }
}
