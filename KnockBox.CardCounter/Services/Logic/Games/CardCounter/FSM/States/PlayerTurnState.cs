using KnockBox.Services.State.Games.CardCounter;

namespace KnockBox.Services.Logic.Games.CardCounter.FSM.States
{
    /// <summary>
    /// Active play phase: the current player may play action cards, fold, draw, or pass.
    /// </summary>
    public sealed class PlayerTurnState : ICardCounterGameState
    {
        public void OnEnter(CardCounterGameContext context)
        {
            context.State.GamePhase = GamePhase.Playing;
            context.State.PendingReaction = null;
            context.State.FeelingLuckyTargetId = null;
            context.State.IsNotMyMoneySelecting = false;
            context.State.PendingNotMyMoneyOperator = null;
            context.Logger.LogInformation(
                "FSM → PlayerTurnState (active: {id})", context.CurrentPlayerId);
        }

        public ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command)
        {
            return command switch
            {
                DrawCardCommand cmd => HandleDraw(context, cmd),
                //PassTurnCommand cmd => HandlePass(context, cmd), // disabled
                //FoldPotCommand cmd => HandleFold(context, cmd), // disabled
                PlayActionCardCommand cmd => HandlePlayActionCard(context, cmd),
                _ => null
            };
        }

        public ICardCounterGameState? Tick(CardCounterGameContext context, DateTimeOffset now) => null;

        // ── Draw ──────────────────────────────────────────────────────────────

        private ICardCounterGameState? HandleDraw(CardCounterGameContext context, DrawCardCommand cmd)
        {
            if (!context.IsCurrentPlayer(cmd.PlayerId))
            {
                context.Logger.LogWarning("Draw: [{id}] is not the active player.", cmd.PlayerId);
                return null;
            }

            if (context.CurrentShoe.Count == 0)
            {
                context.Logger.LogWarning("Draw: shoe is empty.");
                return null;
            }

            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null) return null;

            var card = context.CurrentShoe.Pop();
            context.DiscardPile.Push(card);
            context.DecrementShoeCount(card);

            // If the player holds Not My Money and this is an operator, enter the redirect state
            if (card is OperatorCard oc && player.ActionHand.Any(c => c.Action == ActionType.NotMyMoney))
            {
                // Record the draw for the discard history (will be finalised in NotMyMoneyState)
                return new NotMyMoneyState(cmd.PlayerId, oc);
            }

            if (card is NumberCard nc)
            {
                context.ApplyNumberCard(player, nc);
            }
            else if (card is OperatorCard operatorCard)
            {
                context.ApplyOperatorCard(player, operatorCard);
            }

            context.RecordDraw(player, card);

            context.AdvanceTurn();

            // End of shoe → deal next shoe / end game
            if (context.CurrentShoe.Count == 0)
                return new RoundEndState();

            return null; // stay in PlayerTurnState
        }

        // ── Pass ─────────────────────────────────────────────────────────────

        private ICardCounterGameState? HandlePass(CardCounterGameContext context, PassTurnCommand cmd)
        {
            if (!context.IsCurrentPlayer(cmd.PlayerId))
            {
                context.Logger.LogWarning("Pass: [{id}] is not the active player.", cmd.PlayerId);
                return null;
            }

            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null) return null;

            if (player.PassesRemaining <= 0)
            {
                context.Logger.LogWarning("Pass: player [{id}] has no passes remaining.", cmd.PlayerId);
                return null;
            }

            player.PassesRemaining--;
            context.AdvanceTurn();

            if (context.CurrentShoe.Count == 0)
                return new RoundEndState();

            return null;
        }

        // ── Fold ──────────────────────────────────────────────────────────────

        private ICardCounterGameState? HandleFold(CardCounterGameContext context, FoldPotCommand cmd)
        {
            if (!context.IsCurrentPlayer(cmd.PlayerId))
            {
                context.Logger.LogWarning("Fold: [{id}] is not the active player.", cmd.PlayerId);
                return null;
            }

            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null) return null;

            if (player.PassesRemaining <= 0)
            {
                context.Logger.LogWarning("Fold: player [{id}] has no passes remaining.", cmd.PlayerId);
                return null;
            }

            player.PassesRemaining--;
            player.Pot.Clear();
            // Turn does NOT advance after a fold — player still takes their draw/pass action.
            return null;
        }

        // ── Action cards ──────────────────────────────────────────────────────

        private ICardCounterGameState? HandlePlayActionCard(CardCounterGameContext context, PlayActionCardCommand cmd)
        {
            if (!context.IsCurrentPlayer(cmd.PlayerId))
            {
                context.Logger.LogWarning("PlayAction: [{id}] is not the active player.", cmd.PlayerId);
                return null;
            }

            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null) return null;

            if (player.ActionHand.Count > context.Config.ActionHandLimit)
            {
                context.Logger.LogWarning("PlayAction: player [{id}] must discard before playing.", cmd.PlayerId);
                return null;
            }

            if (cmd.CardIndex < 0 || cmd.CardIndex >= player.ActionHand.Count)
            {
                context.Logger.LogWarning("PlayAction: invalid card index {i} for player [{id}].", cmd.CardIndex, cmd.PlayerId);
                return null;
            }

            var card = player.ActionHand[cmd.CardIndex];
            if (card.Action == ActionType.NotMyMoney || card.Action == ActionType.Compd)
            {
                context.Logger.LogWarning("PlayAction: [{id}] cannot play {action} directly.", cmd.PlayerId, card.Action);
                return null;
            }

            player.ActionHand.RemoveAt(cmd.CardIndex);

            context.Logger.LogInformation(
                "Player [{id}] played action card [{action}].", cmd.PlayerId, card.Action);

            context.State.LastPlayedAction = new LastPlayedActionInfo(
                cmd.PlayerId,
                player.DisplayName,
                card.Action,
                cmd.TargetPlayerId,
                cmd.TargetPlayerId is not null ? context.GetPlayer(cmd.TargetPlayerId)?.DisplayName : null);

            context.RecordActionCardPlay(player, card);

            return card.Action switch
            {
                ActionType.FeelingLucky => HandleFeelingLucky(context, cmd.PlayerId),
                ActionType.MakeMyLuck => HandleMakeMyLuck(context, cmd.PlayerId),
                ActionType.Burn => HandleBurn(context),
                ActionType.Skim => HandleSkim(context, cmd),
                ActionType.TurnTheTable => HandleBlockable(context, cmd, card),
                ActionType.Launder => HandleBlockable(context, cmd, card),
                _ => null
            };
        }

        // ── Discard ───────────────────────────────────────────────────────────

        private static ICardCounterGameState? HandleFeelingLucky(CardCounterGameContext context, string sourceId)
        {
            if (context.TurnOrder.Count <= 1)
            {
                context.Logger.LogWarning("FeelingLucky: cannot chain with only one player.");
                return null;
            }

            // Determine the next player in turn order (wrapping) to be the first target
            int nextIndex = (context.State.CurrentPlayerIndex + 1) % context.TurnOrder.Count;
            string targetId = context.TurnOrder[nextIndex];
            return new FeelingLuckyChainState(sourceId, targetId);
        }

        private static ICardCounterGameState? HandleMakeMyLuck(CardCounterGameContext context, string sourceId)
        {
            if (context.CurrentShoe.Count == 0) return null;

            int revealCount = Math.Min(3, context.CurrentShoe.Count);
            var reveal = context.CurrentShoe.Take(revealCount).ToList();

            var player = context.GetPlayer(sourceId);
            if (player is null) return null;

            player.PrivateReveal = reveal;
            return new MakeMyLuckState(sourceId);
        }

        private static ICardCounterGameState? HandleBurn(CardCounterGameContext context)
        {
            if (context.CurrentShoe.Count == 0) return null;

            var top = context.CurrentShoe.Pop();
            context.DiscardPile.Push(top);
            context.DecrementShoeCount(top);
            context.RecordBurn(top);
            return null;
        }

        private static ICardCounterGameState? HandleBlockable(
            CardCounterGameContext context, PlayActionCardCommand cmd, ActionCard card)
        {
            if (string.IsNullOrEmpty(cmd.TargetPlayerId))
            {
                context.Logger.LogWarning("Blockable action [{a}] requires a target.", card.Action);
                return null;
            }

            var target = context.GetPlayer(cmd.TargetPlayerId);
            if (target is null)
            {
                context.Logger.LogWarning("Blockable action [{a}]: target [{id}] not found.", card.Action, cmd.TargetPlayerId);
                return null;
            }

            // Self-targeting: apply the effect immediately without a reaction window.
            if (cmd.PlayerId == cmd.TargetPlayerId)
            {
                context.Logger.LogInformation(
                    "Blockable action [{a}]: self-targeted by [{id}]; applying immediately.", card.Action, cmd.PlayerId);
                switch (card.Action)
                {
                    case ActionType.TurnTheTable:
                        target.Pot.Reverse();
                        break;
                    case ActionType.Launder:
                        // Self-launder: swapping the pot with itself is a no-op.
                        break;
                }
                return new PlayerTurnState();
            }

            return new WaitingForReactionState(cmd.PlayerId, cmd.TargetPlayerId, card);
        }

        private static ICardCounterGameState? HandleSkim(
            CardCounterGameContext context, PlayActionCardCommand cmd)
        {
            if (string.IsNullOrEmpty(cmd.TargetPlayerId))
            {
                context.Logger.LogWarning("Skim requires a target.");
                return null;
            }

            var source = context.GetPlayer(cmd.PlayerId);
            var target = context.GetPlayer(cmd.TargetPlayerId);

            if (source is null || target is null)
            {
                context.Logger.LogWarning("Skim: source or target not found.");
                return null;
            }

            if (source.Pot.Count == 0)
            {
                context.Logger.LogWarning("Skim: [{id}] cannot play Skim with an empty pot.", cmd.PlayerId);
                return null;
            }

            if (target.Pot.Count == 0)
            {
                context.Logger.LogWarning("Skim: target [{id}] has an empty pot; cannot target them.", cmd.TargetPlayerId);
                return null;
            }

            // Build the SkimState which lets the source choose digit indices
            return new SkimState(cmd.PlayerId, cmd.TargetPlayerId, new ActionCard(ActionType.Skim));
        }
    }
}
