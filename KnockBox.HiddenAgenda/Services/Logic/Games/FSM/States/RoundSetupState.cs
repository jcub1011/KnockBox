using System;
using System.Linq;
using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.State.Games;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States
{
    public sealed class RoundSetupState : ITimedHiddenAgendaGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(HiddenAgendaGameContext context)
        {
            context.State.SetPhase(GamePhase.RoundSetup);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.RoundSetupTimeoutMs);

            context.State.CurrentRound++;
            context.State.CurrentTaskPool = TaskPool.GetPoolForPlayerCount(context.GamePlayers.Count);

            foreach (var playerId in context.GamePlayers.Keys)
            {
                context.DrawTasksForPlayer(playerId);
            }

            // Randomize turn order on first round
            if (context.State.CurrentRound == 1)
            {
                var playerIds = context.GamePlayers.Keys.OrderBy(_ => context.Rng.GetRandomInt(10000)).ToList();
                context.State.TurnManager.SetTurnOrder(playerIds);
            }

            // Initialize collection progress
            context.State.CollectionProgress.Clear();
            foreach (var type in Enum.GetValues<CollectionType>())
            {
                context.State.CollectionProgress[type] = 0;
            }

            // Place all players at starting position
            foreach (var player in context.GamePlayers.Values)
            {
                player.CurrentSpaceId = 0;
            }

            return null;
        }

        public Result OnExit(HiddenAgendaGameContext context) => Result.Success;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleCommand(HiddenAgendaGameContext context, HiddenAgendaCommand command) => null;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> Tick(HiddenAgendaGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;
            return new EventCardPhaseState();
        }

        public ValueResult<TimeSpan> GetRemainingTime(HiddenAgendaGameContext context, DateTimeOffset now) => _expiresAt - now;
    }
}