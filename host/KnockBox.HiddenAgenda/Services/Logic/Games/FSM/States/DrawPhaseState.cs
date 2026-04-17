using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;

namespace KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States
{
    public sealed class DrawPhaseState : ITimedHiddenAgendaGameState
    {
        private DateTimeOffset _expiresAt;
        private int? _pendingTradeCardIndex;

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> OnEnter(HiddenAgendaGameContext context)
        {
            var currentPlayerId = context.State.TurnManager.CurrentPlayer;
            if (currentPlayerId == null || !context.GamePlayers.TryGetValue(currentPlayerId, out var player))
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(context.AdvanceToNextPlayerOrEndRound());
            }

            var space = context.Board.Spaces[player.CurrentSpaceId];
            context.State.SetPhase(GamePhase.DrawPhase);
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.State.Config.DrawPhaseTimeoutMs);

            if (space.SpotType == SpotType.Curation)
            {
                var drawn = CurationCardPool.DrawThree(context.Rng, space.Wing);
                context.State.DrawnCards = drawn;
                player.CardDrawHistory.Add(new CardDrawRecord(player.TurnsTakenThisRound + 1, drawn));
            }
            else if (space.SpotType == SpotType.Event)
            {
                // Draw random event card (50/50 Catalog or Detour)
                var eventCard = context.Rng.GetRandomInt(2) == 0 ? EventCardDefinitions.Catalog : EventCardDefinitions.Detour;
                
                if (player.HeldEventCard == null)
                {
                    player.HeldEventCard = eventCard;
                    return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(FinishTurn(context, player));
                }
                else
                {
                    context.State.PendingDrawnEventCard = eventCard;
                }
            }

            return null;
        }

        public Result OnExit(HiddenAgendaGameContext context)
        {
            context.State.DrawnCards = null;
            context.State.PendingDrawnEventCard = null;
            return Result.Success;
        }

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
                case SelectCurationCardCommand selectCuration:
                    return HandleSelectCurationCard(context, player, selectCuration);
                case SelectTradeOptionCommand selectTrade:
                    return HandleSelectTradeOption(context, player, selectTrade);
                case SelectEventCardActionCommand selectEvent:
                    return HandleSelectEventCardAction(context, player, selectEvent);
                default:
                    return null;
            }
        }

        private ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleSelectCurationCard(HiddenAgendaGameContext context, HiddenAgendaPlayerState player, SelectCurationCardCommand cmd)
        {
            if (context.State.DrawnCards == null || cmd.CardIndex < 0 || cmd.CardIndex >= context.State.DrawnCards.Count)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("Invalid card index.");
            }

            var card = context.State.DrawnCards[cmd.CardIndex];
            if (card.Type == CurationCardType.Trade && card.AlternateEffects != null)
            {
                _pendingTradeCardIndex = cmd.CardIndex;
                return null; // Wait for SelectTradeOptionCommand
            }

            context.ApplyCollectionEffects(card.Effects);
            context.RecordCardPlay(player.PlayerId, card, cmd.CardIndex, context.State.DrawnCards, card.Effects);
            
            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(FinishTurn(context, player));
        }

        private ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleSelectTradeOption(HiddenAgendaGameContext context, HiddenAgendaPlayerState player, SelectTradeOptionCommand cmd)
        {
            if (_pendingTradeCardIndex == null || context.State.DrawnCards == null)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("No trade card selected.");
            }

            var card = context.State.DrawnCards[_pendingTradeCardIndex.Value];
            var effects = cmd.UseAlternate && card.AlternateEffects != null ? card.AlternateEffects : card.Effects;

            context.ApplyCollectionEffects(effects);
            context.RecordCardPlay(player.PlayerId, card, _pendingTradeCardIndex.Value, context.State.DrawnCards, effects);

            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(FinishTurn(context, player));
        }

        private ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> HandleSelectEventCardAction(HiddenAgendaGameContext context, HiddenAgendaPlayerState player, SelectEventCardActionCommand cmd)
        {
            if (context.State.PendingDrawnEventCard == null)
            {
                return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromError("No event card pending.");
            }

            if (cmd.KeepNewCard)
            {
                player.HeldEventCard = context.State.PendingDrawnEventCard;
            }

            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(FinishTurn(context, player));
        }

        private IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>? FinishTurn(HiddenAgendaGameContext context, HiddenAgendaPlayerState player)
        {
            player.TurnsTakenThisRound++;
            context.State.TotalTurnsTaken++;

            return context.AdvanceToNextPlayerOrEndRound();
        }

        public ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?> Tick(HiddenAgendaGameContext context, DateTimeOffset now)
        {
            if (now < _expiresAt) return null;
            if (!context.State.Config.EnableTimers) return null;

            var currentPlayerId = context.State.TurnManager.CurrentPlayer;
            if (currentPlayerId != null && context.GamePlayers.TryGetValue(currentPlayerId, out var player))
            {
                if (context.State.DrawnCards != null)
                {
                    // Auto-select first card, use primary effects if trade
                    var card = context.State.DrawnCards[0];
                    context.ApplyCollectionEffects(card.Effects);
                    context.RecordCardPlay(player.PlayerId, card, 0, context.State.DrawnCards, card.Effects);
                    return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(FinishTurn(context, player));
                }
                else if (context.State.PendingDrawnEventCard != null)
                {
                    // Auto-keep new event card
                    player.HeldEventCard = context.State.PendingDrawnEventCard;
                    return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(FinishTurn(context, player));
                }
            }

            return ValueResult<IGameState<HiddenAgendaGameContext, HiddenAgendaCommand>?>.FromValue(context.AdvanceToNextPlayerOrEndRound());
        }

        public ValueResult<TimeSpan> GetRemainingTime(HiddenAgendaGameContext context, DateTimeOffset now) => _expiresAt - now;
    }
}
