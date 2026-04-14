using System;
using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States
{
    public sealed class GuessPhaseState : ITimedHiddenAgendaGameState
    {
        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(HiddenAgendaGameContext context)
        {
            // Placeholder: skip guessing, use shared helper to advance
            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(context.AdvanceToNextPlayerOrEndRound());
        }

        public Result OnExit(HiddenAgendaGameContext context) => Result.Success;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleCommand(HiddenAgendaGameContext context, HiddenAgendaCommand command) => null;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> Tick(HiddenAgendaGameContext context, DateTimeOffset now) => null;

        public ValueResult<TimeSpan> GetRemainingTime(HiddenAgendaGameContext context, DateTimeOffset now) => TimeSpan.Zero;
    }
}