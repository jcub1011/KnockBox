using KnockBox.Services.State.Games.CardCounter;

namespace KnockBox.Services.Logic.Games.CardCounter.FSM.States
{
    /// <summary>
    /// Processes the Feeling Lucky chain: the current target must either draw a card,
    /// pass the force to the next player with another Feeling Lucky card, or play Comp'd.
    /// Once resolved the game resumes from the originator.
    /// </summary>
    public sealed class FeelingLuckyChainState : ICardCounterGameState
    {
        private readonly string _originatorId;
        private string _currentTargetId;
        private DateTimeOffset _expiresAt;

        public FeelingLuckyChainState(string originatorId, string firstTargetId)
        {
            _originatorId = originatorId;
            _currentTargetId = firstTargetId;
        }

        public void OnEnter(CardCounterGameContext context)
        {
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.Config.ActionResponseTimeoutMs);
            context.State.FeelingLuckyTargetId = _currentTargetId;
            context.Logger.LogInformation(
                "FSM → FeelingLuckyChainState: originator [{orig}], target [{tgt}].",
                _originatorId, _currentTargetId);
        }

        public ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command)
        {
            if (command.PlayerId != _currentTargetId)
                return null;

            if (command is DrawCardCommand)
                return ForceTargetDraw(context);

            if (command is PlayActionCardCommand playCmd)
                return HandleTargetAction(context, playCmd);

            return null;
        }

        public ICardCounterGameState? Tick(CardCounterGameContext context, DateTimeOffset now)
        {
            if (now >= _expiresAt)
            {
                context.Logger.LogInformation(
                    "FeelingLucky: timeout; forcing draw for [{tgt}].", _currentTargetId);
                return ForceTargetDraw(context);
            }
            return null;
        }

        private ICardCounterGameState? HandleTargetAction(
            CardCounterGameContext context, PlayActionCardCommand cmd)
        {
            var target = context.GetPlayer(_currentTargetId);
            if (target is null || cmd.CardIndex < 0 || cmd.CardIndex >= target.ActionHand.Count)
                return null;

            var card = target.ActionHand[cmd.CardIndex];

            if (card.Action == ActionType.Compd)
            {
                // Block — the player before them in the chain must still draw or pass.
                target.ActionHand.RemoveAt(cmd.CardIndex);
                context.RecordActionCardPlay(target, card);
                context.Logger.LogInformation("FeelingLucky: [{id}] blocked with Comp'd.", _currentTargetId);
                // Chain is resolved — return to originator's turn
                return ReturnToOriginator(context);
            }

            if (card.Action == ActionType.FeelingLucky)
            {
                // Pass the force to the next player in turn order
                target.ActionHand.RemoveAt(cmd.CardIndex);
                context.RecordActionCardPlay(target, card);
                int currentIdx = context.TurnOrder.IndexOf(_currentTargetId);
                int nextIdx = (currentIdx + 1) % context.TurnOrder.Count;
                string nextTarget = context.TurnOrder[nextIdx];

                // Skip back to originator if the chain wraps all the way around
                if (nextTarget == _originatorId)
                    return ForceTargetDraw(context);

                context.Logger.LogInformation(
                    "FeelingLucky: chain passed from [{from}] to [{to}].", _currentTargetId, nextTarget);
                _currentTargetId = nextTarget;
                context.State.FeelingLuckyTargetId = _currentTargetId;
                _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.Config.ActionResponseTimeoutMs);
                return null; // stay in this state, target changed
            }

            return null;
        }

        private ICardCounterGameState ForceTargetDraw(CardCounterGameContext context)
        {
            if (context.CurrentShoe.Count > 0)
            {
                var target = context.GetPlayer(_currentTargetId);
                if (target is not null)
                {
                    var card = context.CurrentShoe.Pop();
                    context.DiscardPile.Push(card);
                    context.DecrementShoeCount(card);

                    if (card is NumberCard nc)
                        context.ApplyNumberCard(target, nc);
                    else if (card is OperatorCard oc)
                        context.ApplyOperatorCard(target, oc);

                    context.RecordDraw(target, card);

                    context.Logger.LogInformation(
                        "FeelingLucky: [{id}] force-drew [{card}].", _currentTargetId, card);
                }
            }

            return ReturnToOriginator(context);
        }

        private ICardCounterGameState ReturnToOriginator(CardCounterGameContext context)
        {
            // Restore turn pointer to the originator
            int idx = context.TurnOrder.IndexOf(_originatorId);
            if (idx >= 0) context.State.CurrentPlayerIndex = idx;

            if (context.CurrentShoe.Count == 0)
                return new RoundEndState();

            return new PlayerTurnState();
        }
    }
}
