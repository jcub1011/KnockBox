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
            context.Logger.LogInformation(
                "FSM → PlayerTurnState (active: {id})", context.CurrentPlayerId);
        }

        public ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command)
        {
            return command switch
            {
                DrawCardCommand cmd => HandleDraw(context, cmd),
                PassTurnCommand cmd => HandlePass(context, cmd),
                FoldPotCommand cmd => HandleFold(context, cmd),
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

            if (card is NumberCard nc)
            {
                context.ApplyNumberCard(player, nc);
            }
            else if (card is OperatorCard oc)
            {
                context.ApplyOperatorCard(player, oc);
            }

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

            if (cmd.CardIndex < 0 || cmd.CardIndex >= player.ActionHand.Count)
            {
                context.Logger.LogWarning("PlayAction: invalid card index {i} for player [{id}].", cmd.CardIndex, cmd.PlayerId);
                return null;
            }

            var card = player.ActionHand[cmd.CardIndex];
            player.ActionHand.RemoveAt(cmd.CardIndex);

            context.Logger.LogInformation(
                "Player [{id}] played action card [{action}].", cmd.PlayerId, card.Action);

            return card.Action switch
            {
                ActionType.FeelingLucky => HandleFeelingLucky(context, cmd.PlayerId),
                ActionType.MakeMyLuck => HandleMakeMyLuck(context, cmd.PlayerId),
                ActionType.Burn => HandleBurn(context),
                ActionType.Skim => HandleBlockable(context, cmd, card),
                ActionType.TurnTheTable => HandleBlockable(context, cmd, card),
                ActionType.Launder => HandleBlockable(context, cmd, card),
                ActionType.NotMyMoney => null, // Played on draw — not handled here
                ActionType.Compd => null,      // Only valid as a response
                _ => null
            };
        }

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

            return new WaitingForReactionState(cmd.PlayerId, cmd.TargetPlayerId, card);
        }
    }
}
