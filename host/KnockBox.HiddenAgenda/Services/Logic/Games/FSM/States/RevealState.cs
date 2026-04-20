using System;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.HiddenAgenda.Services.State.Games;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States
{
    public sealed class RevealState : ITimedHiddenAgendaGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(HiddenAgendaGameContext context)
        {
            // Evaluate all tasks for all players
            var roundResult = context.ScoreRound();
            context.State.RoundResults.Add(roundResult);

            context.State.SetPhase(GamePhase.Reveal);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(
                context.State.Config.RevealTimeoutMs);
            context.State.PhaseEndTime = _expiresAt;
            return null;
        }

        public Result OnExit(HiddenAgendaGameContext context) => Result.Success;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleCommand(
            HiddenAgendaGameContext context, HiddenAgendaCommand command) => null; // No commands during reveal

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> Tick(HiddenAgendaGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;
            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new RoundOverState());
        }

        public ValueResult<TimeSpan> GetRemainingTime(HiddenAgendaGameContext context, DateTimeOffset now) => _expiresAt - now;
    }
}
