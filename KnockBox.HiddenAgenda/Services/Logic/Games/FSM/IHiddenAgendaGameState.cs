using KnockBox.Core.Services.State.Games.Shared.Interfaces;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM
{
    public interface IHiddenAgendaGameState
        : IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>;

    public interface ITimedHiddenAgendaGameState
        : ITimedGameState<HiddenAgendaGameContext, HiddenAgendaCommand>;
}
