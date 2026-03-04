using KnockBox.Services.State.Games.CardCounter;

namespace KnockBox.Services.Logic.Games.CardCounter.FSM.States
{
    /// <summary>
    /// Waiting for the target of a blockable action card (Skim, Turn The Table, Launder)
    /// to respond. The target may play Comp'd to negate the effect or accept it.
    /// A server-side timeout causes automatic acceptance.
    /// </summary>
    public sealed class WaitingForReactionState : ICardCounterGameState
    {
        private readonly string _sourceId;
        private readonly string _targetId;
        private readonly ActionCard _pendingCard;
        private DateTimeOffset _expiresAt;

        public WaitingForReactionState(string sourceId, string targetId, ActionCard pendingCard)
        {
            _sourceId = sourceId;
            _targetId = targetId;
            _pendingCard = pendingCard;
        }

        public void OnEnter(CardCounterGameContext context)
        {
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.Config.ActionResponseTimeoutMs);
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

        private ICardCounterGameState ResolveEffect(CardCounterGameContext context, bool blocked)
        {
            if (!blocked)
            {
                ApplyEffect(context);
            }
            return new PlayerTurnState();
        }

        private void ApplyEffect(CardCounterGameContext context)
        {
            var source = context.GetPlayer(_sourceId);
            var target = context.GetPlayer(_targetId);
            if (source is null || target is null) return;

            switch (_pendingCard.Action)
            {
                case ActionType.Skim:
                    // A full Skim needs digit-position info from the original command.
                    // For now we swap the last digit of each pot if available.
                    if (source.Pot.Count > 0 && target.Pot.Count > 0)
                    {
                        int si = source.Pot.Count - 1;
                        int ti = target.Pot.Count - 1;
                        (source.Pot[si], target.Pot[ti]) = (target.Pot[ti], source.Pot[si]);
                    }
                    break;

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
            }

            context.Logger.LogInformation(
                "Action [{card}] applied: [{src}] → [{tgt}].",
                _pendingCard.Action, _sourceId, _targetId);
        }
    }
}
