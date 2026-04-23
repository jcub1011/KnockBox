using System;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using System.Linq;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States
{
    public sealed class EventCardPhaseState : ITimedHiddenAgendaGameState
    {
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(HiddenAgendaGameContext context)
        {
            var currentPlayerId = context.State.TurnManager.CurrentPlayer;
            if (currentPlayerId == null || !context.GamePlayers.TryGetValue(currentPlayerId, out var player))
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(context.AdvanceToNextPlayerOrEndRound());
            }

            if (player.HeldEventCard == null)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new SpinPhaseState());
            }

            context.State.SetPhase(GamePhase.EventCardPhase);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.EventCardPhaseTimeoutMs);
            return null;
        }

        public Result OnExit(HiddenAgendaGameContext context) => Result.Success;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleCommand(HiddenAgendaGameContext context, HiddenAgendaCommand command)
        {
            if (command is CallVoteCommand)
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new FinalGuessState());

            var currentPlayerId = context.State.TurnManager.CurrentPlayer;
            if (command.PlayerId != currentPlayerId)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("It is not your turn.");
            }

            if (!context.GamePlayers.TryGetValue(currentPlayerId, out var player))
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Current player not found.");
            }

            switch (command)
            {
                case PlayCatalogCommand playCatalog:
                    return HandlePlayCatalog(context, player, playCatalog);
                case PlayDetourCommand playDetour:
                    return HandlePlayDetour(context, player, playDetour);
                case SkipEventCardCommand _:
                    return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new SpinPhaseState());
                default:
                    return null;
            }
        }

        private ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandlePlayCatalog(HiddenAgendaGameContext context, HiddenAgendaPlayerState player, PlayCatalogCommand cmd)
        {
            if (player.HeldEventCard?.Type != EventCardType.Catalog)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("You do not hold a Catalog card.");
            }

            if (cmd.TargetPlayerId == player.PlayerId)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("You cannot target yourself.");
            }

            if (!context.GamePlayers.TryGetValue(cmd.TargetPlayerId, out var target))
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Target player not found.");
            }

            // Catalog reveals target's last draw history
            var lastDraw = target.CardDrawHistory.LastOrDefault();
            context.State.CatalogRevealedCards = lastDraw?.DrawnCards.ToList();
            
            player.HeldEventCard = null;
            
            // Catalog ends turn
            player.TurnsTakenThisRound++;
            context.State.TotalTurnsTaken++;
            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(context.AdvanceToNextPlayerOrEndRound());
        }

        private ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandlePlayDetour(HiddenAgendaGameContext context, HiddenAgendaPlayerState player, PlayDetourCommand cmd)
        {
            if (player.HeldEventCard?.Type != EventCardType.Detour)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("You do not hold a Detour card.");
            }

            if (cmd.TargetPlayerId == player.PlayerId)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("You cannot target yourself.");
            }

            if (!context.GamePlayers.TryGetValue(cmd.TargetPlayerId, out var target))
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Target player not found.");
            }

            if (target.LastMoveDestination == null)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Target player hasn't moved yet.");
            }

            player.DetourTargetPlayerId = cmd.TargetPlayerId;
            player.DetourPending = true;
            player.HeldEventCard = null;

            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new SpinPhaseState());
        }

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> Tick(HiddenAgendaGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;
            if (!context.State.Config.EnableTimers) return null;
            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(new SpinPhaseState());
        }

        public ValueResult<TimeSpan> GetRemainingTime(HiddenAgendaGameContext context, DateTimeOffset now) => _expiresAt - now;
    }
}
