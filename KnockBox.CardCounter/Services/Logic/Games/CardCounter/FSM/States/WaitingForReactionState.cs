using KnockBox.Services.State.Games.CardCounter;

namespace KnockBox.Services.Logic.Games.CardCounter.FSM.States
{
    /// <summary>
    /// Waiting for the target of a blockable action card (Skim, Turn The Table, Launder)
    /// to respond. The target may play Comp'd to negate the effect or accept it.
    /// A server-side timeout causes automatic acceptance.
    /// </summary>
    public sealed class WaitingForReactionState : ITimedCardCounterGameState
    {
        private readonly string _sourceId;
        private readonly string _targetId;
        private readonly ActionCard _pendingCard;
        private readonly OperatorCard? _notMyMoneyOperator;
        private DateTimeOffset _expiresAt;

        public WaitingForReactionState(string sourceId, string targetId, ActionCard pendingCard, OperatorCard? notMyMoneyOperator = null)
        {
            _sourceId = sourceId;
            _targetId = targetId;
            _pendingCard = pendingCard;
            _notMyMoneyOperator = notMyMoneyOperator;
        }

        public void OnEnter(CardCounterGameContext context)
        {
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.Config.WaitingForReactionTimeoutMs);
            context.State.PendingReaction = new PendingReactionInfo(
                _sourceId,
                context.GetPlayer(_sourceId)?.DisplayName ?? _sourceId,
                _targetId,
                _pendingCard,
                NotMyMoneyOperator: _notMyMoneyOperator?.Op);
            context.Logger.LogInformation(
                "FSM → WaitingForReactionState: [{src}] played [{card}] on [{tgt}]. Expires {exp}.",
                _sourceId, _pendingCard.Action, _targetId, _expiresAt);
        }

        public ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command)
        {
            if (command is PlayActionCardCommand playCmd && playCmd.PlayerId == _targetId)
            {
                var target = context.GetPlayer(_targetId);
                if (target is null) return ResolveEffect(context, blocked: false);

                if (playCmd.CardIndex < 0 || playCmd.CardIndex >= target.ActionHand.Count)
                    return null;

                var responseCard = target.ActionHand[playCmd.CardIndex];
                if (responseCard.Action != ActionType.Compd)
                    return null;

                // Target blocked with Comp'd
                target.ActionHand.RemoveAt(playCmd.CardIndex);
                context.State.LastPlayedAction = new LastPlayedActionInfo(
                    target.PlayerId,
                    target.DisplayName,
                    responseCard.Action,
                    null,
                    null);
                context.RecordActionCardPlay(target, responseCard);
                context.Logger.LogInformation("Player [{id}] blocked with Comp'd.", _targetId);
                return ResolveEffect(context, blocked: true);
            }

            if (command is AcceptPendingCommand acceptCmd && acceptCmd.PlayerId == _targetId)
                return ResolveEffect(context, blocked: false);

            return null;
        }

        public ICardCounterGameState? Tick(CardCounterGameContext context, DateTimeOffset now)
        {
            if (now >= _expiresAt)
            {
                context.Logger.LogInformation(
                    "Reaction window expired for [{tgt}]; auto-accepting.", _targetId);
                return ResolveEffect(context, blocked: false);
            }
            return null;
        }

        public TimeSpan GetRemainingTime(CardCounterGameContext context, DateTimeOffset now) => _expiresAt - now;

        private ICardCounterGameState ResolveEffect(CardCounterGameContext context, bool blocked)
        {
            if (!blocked)
            {
                ApplyEffect(context);

                if (_pendingCard.Action == ActionType.NotMyMoney && _notMyMoneyOperator != null)
                {
                    return FinishTurn(context);
                }
            }
            else
            {
                if (_pendingCard.Action == ActionType.NotMyMoney && _notMyMoneyOperator != null)
                {
                    var source = context.GetPlayer(_sourceId);
                    if (source != null)
                    {
                        if (source.ActionHand.Any(c => c.Action == ActionType.NotMyMoney))
                        {
                            return new NotMyMoneyState(_sourceId, _notMyMoneyOperator);
                        }
                        else
                        {
                            // Could not play another Not My Money, so the player eats the operator
                            context.RecordDraw(source, _notMyMoneyOperator);
                            context.ApplyOperatorCard(source, _notMyMoneyOperator);
                            return FinishTurn(context);
                        }
                    }
                }
            }

            return new PlayerTurnState();
        }

        private ICardCounterGameState FinishTurn(CardCounterGameContext context)
        {
            context.AdvanceTurn();

            if (context.CurrentShoe.Count == 0)
                return new RoundEndState();

            return new PlayerTurnState();
        }

        private void ApplyEffect(CardCounterGameContext context)
        {
            var source = context.GetPlayer(_sourceId);
            var target = context.GetPlayer(_targetId);
            if (source is null || target is null) return;

            switch (_pendingCard.Action)
            {
                case ActionType.TurnTheTable:
                    target.Pot.Reverse();
                    break;

                case ActionType.Launder:
                    var tempPot = new List<int>(source.Pot);
                    source.Pot.Clear();
                    source.Pot.AddRange(target.Pot);
                    target.Pot.Clear();
                    target.Pot.AddRange(tempPot);
                    break;

                case ActionType.NotMyMoney:
                    if (_notMyMoneyOperator != null)
                    {
                        context.RecordRedirectedDraw(source, target, _notMyMoneyOperator);
                        context.ApplyOperatorCard(target, _notMyMoneyOperator);
                    }
                    break;
            }

            context.Logger.LogInformation(
                "Action [{card}] applied: [{src}] → [{tgt}].",
                _pendingCard.Action, _sourceId, _targetId);
        }
    }
}
