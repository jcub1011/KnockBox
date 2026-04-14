using System;
using System.Linq;
using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States
{
    public sealed class MovePhaseState : ITimedHiddenAgendaGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(HiddenAgendaGameContext context)
        {
            var currentPlayerId = context.State.TurnManager.CurrentPlayer;
            if (currentPlayerId == null || !context.GamePlayers.TryGetValue(currentPlayerId, out var player))
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(context.AdvanceToNextPlayerOrEndRound());
            }

            context.State.SetPhase(GamePhase.MovePhase);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.MovePhaseTimeoutMs);

            context.State.ReachableSpaces = context.Board.GetReachableSpaces(player.CurrentSpaceId, player.LastSpinResult).ToList();
            
            return null;
        }

        public Result OnExit(HiddenAgendaGameContext context)
        {
            context.State.ReachableSpaces = null;
            context.State.CurrentSpinResult = null;
            return Result.Success;
        }

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleCommand(HiddenAgendaGameContext context, HiddenAgendaCommand command)
        {
            if (command is not SelectDestinationCommand selectDestination) return null;

            var currentPlayerId = context.State.TurnManager.CurrentPlayer;
            if (command.PlayerId != currentPlayerId)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("It is not your turn.");
            }

            if (!context.GamePlayers.TryGetValue(currentPlayerId, out var player))
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Current player not found.");
            }

            return ResolveMove(context, player, selectDestination.DestinationSpaceId);
        }

        private ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> ResolveMove(HiddenAgendaGameContext context, HiddenAgendaPlayerState player, int destinationId)
        {
            if (context.State.ReachableSpaces == null || !context.State.ReachableSpaces.Any(s => s.Id == destinationId))
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Invalid destination.");
            }

            player.CurrentSpaceId = destinationId;
            player.LastMoveDestination = destinationId;
            var space = context.Board.Spaces[destinationId];
            player.MovementHistory.Add(new MovementRecord(
                player.TurnsTakenThisRound + 1,
                destinationId,
                space.Wing,
                player.LastSpinResult));

            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new DrawPhaseState());
        }

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> Tick(HiddenAgendaGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;

            var currentPlayerId = context.State.TurnManager.CurrentPlayer;
            if (currentPlayerId != null && context.GamePlayers.TryGetValue(currentPlayerId, out var player))
            {
                var destinationId = context.State.ReachableSpaces?.FirstOrDefault()?.Id ?? player.CurrentSpaceId;
                return ResolveMove(context, player, destinationId);
            }

            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(context.AdvanceToNextPlayerOrEndRound());
        }

        public ValueResult<TimeSpan> GetRemainingTime(HiddenAgendaGameContext context, DateTimeOffset now) => _expiresAt - now;
    }
}