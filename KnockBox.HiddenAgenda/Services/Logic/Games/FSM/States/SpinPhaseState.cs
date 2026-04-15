using System;
using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States
{
    public sealed class SpinPhaseState : ITimedHiddenAgendaGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(HiddenAgendaGameContext context)
        {
            context.State.SetPhase(GamePhase.SpinPhase);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.SpinPhaseTimeoutMs);
            return null;
        }

        public Result OnExit(HiddenAgendaGameContext context) => Result.Success;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleCommand(HiddenAgendaGameContext context, HiddenAgendaCommand command)
        {
            if (command is CallVoteCommand)
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new FinalGuessState());

            if (command is not SpinCommand) return null;

            var currentPlayerId = context.State.TurnManager.CurrentPlayer;
            if (command.PlayerId != currentPlayerId)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("It is not your turn.");
            }

            if (!context.GamePlayers.TryGetValue(currentPlayerId, out var player))
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Current player not found.");
            }

            return ResolveSpin(context, player);
        }

        private ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> ResolveSpin(HiddenAgendaGameContext context, HiddenAgendaPlayerState player)
        {
            if (player.DetourPending && player.DetourTargetPlayerId != null)
            {
                if (context.GamePlayers.TryGetValue(player.DetourTargetPlayerId, out var target) && target.LastMoveDestination != null)
                {
                    player.LastSpinResult = target.LastSpinResult;
                    context.State.CurrentSpinResult = target.LastSpinResult;
                    player.CurrentSpaceId = target.LastMoveDestination.Value;
                    
                    // Record movement history for the detour
                    var space = context.Board.Spaces[player.CurrentSpaceId];
                    player.MovementHistory.Add(new MovementRecord(
                        player.TurnsTakenThisRound + 1,
                        player.CurrentSpaceId,
                        space.Wing,
                        player.LastSpinResult));
                    
                    player.LastMoveDestination = player.CurrentSpaceId;

                    player.DetourPending = false;
                    player.DetourTargetPlayerId = null;

                    return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new DrawPhaseState());
                }
            }

            player.LastSpinResult = context.SpinSpinner();
            context.State.CurrentSpinResult = player.LastSpinResult;
            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new MovePhaseState());
        }

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> Tick(HiddenAgendaGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;
            if (!context.State.Config.EnableTimers) return null;

            var currentPlayerId = context.State.TurnManager.CurrentPlayer;
            if (currentPlayerId != null && context.GamePlayers.TryGetValue(currentPlayerId, out var player))
            {
                return ResolveSpin(context, player);
            }

            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(context.AdvanceToNextPlayerOrEndRound());
        }

        public ValueResult<TimeSpan> GetRemainingTime(HiddenAgendaGameContext context, DateTimeOffset now) => _expiresAt - now;
    }
}