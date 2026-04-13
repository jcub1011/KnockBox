using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Extensions.Returns;
using KnockBox.CardCounter.Services.State.Games;

namespace KnockBox.CardCounter.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Waiting for the Skim source player to select which digit in their pot to swap
    /// with a digit in the target's pot. The target may still block with Comp'd.
    /// </summary>
    public sealed class SkimState : ITimedCardCounterGameState
    {
        private readonly string _sourceId;
        private readonly string _targetId;
        private readonly ActionCard _pendingCard;
        private DateTimeOffset _expiresAt;
        private bool _targetAccepted;
        private bool _sourceSelected;
        private int _selectedSourceDigit = -1;
        private int _selectedTargetDigit = -1;

        public SkimState(string sourceId, string targetId, ActionCard pendingCard)
        {
            _sourceId = sourceId;
            _targetId = targetId;
            _pendingCard = pendingCard;
        }

        public ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> OnEnter(CardCounterGameContext context)
        {
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.Config.SkimTimeoutMs);
            context.State.PendingReaction = new PendingReactionInfo(
                _sourceId,
                context.GetPlayer(_sourceId)?.DisplayName ?? _sourceId,
                _targetId,
                _pendingCard);
            context.Logger.LogInformation(
                "FSM → SkimState: [{src}] selecting digit to swap with [{tgt}]. Expires {exp}.",
                _sourceId, _targetId, _expiresAt);
            return null;
        }

        public Result OnExit(CardCounterGameContext context) => Result.Success;

        public ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> HandleCommand(CardCounterGameContext context, CardCounterCommand command)
        {
            // Target may block with Comp'd
            if (command is PlayActionCardCommand playCmd && playCmd.PlayerId == _targetId)
            {
                var target = context.GetPlayer(_targetId);
                if (target is null) return ResolveEffect(context, -1, -1);

                if (playCmd.CardIndex < 0 || playCmd.CardIndex >= target.ActionHand.Count)
                    return null;

                var responseCard = target.ActionHand[playCmd.CardIndex];
                if (responseCard.Action != ActionType.Compd)
                    return null;

                // Target blocked with Comp'd — cancel the skim
                target.ActionHand.RemoveAt(playCmd.CardIndex);
                context.State.LastPlayedAction = new LastPlayedActionInfo(
                    target.PlayerId,
                    target.DisplayName,
                    responseCard.Action,
                    null,
                    null);
                context.RecordActionCardPlay(target, responseCard);
                context.Logger.LogInformation("Player [{id}] blocked Skim with Comp'd.", _targetId);
                return new PlayerTurnState();
            }

            // Target may also explicitly accept
            if (command is AcceptPendingCommand acceptCmd && acceptCmd.PlayerId == _targetId)
            {
                _targetAccepted = true;
                if (_sourceSelected)
                {
                    return ResolveEffect(context, _selectedSourceDigit, _selectedTargetDigit);
                }
                return null;
            }

            // Source selects digit indices to swap
            if (command is SkimSelectCommand skimCmd && skimCmd.PlayerId == _sourceId)
            {
                _sourceSelected = true;
                _selectedSourceDigit = skimCmd.SourceDigitIndex;
                _selectedTargetDigit = skimCmd.TargetDigitIndex;

                if (context.State.PendingReaction != null)
                {
                    context.State.PendingReaction = context.State.PendingReaction with 
                    { 
                        SourceDigitIndex = _selectedSourceDigit, 
                        TargetDigitIndex = _selectedTargetDigit 
                    };
                }

                if (_targetAccepted)
                {
                    return ResolveEffect(context, _selectedSourceDigit, _selectedTargetDigit);
                }
                return null;
            }

            return null;
        }

        public ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> Tick(CardCounterGameContext context, DateTimeOffset now)
        {
            if (now >= _expiresAt)
            {
                context.Logger.LogInformation(
                    "Skim: timeout; auto-applying last-digit swap for [{src}].", _sourceId);
                // On timeout swap last digits of each pot (default behaviour)
                return ResolveEffect(context, _sourceSelected ? _selectedSourceDigit : -1, _sourceSelected ? _selectedTargetDigit : -1);
            }
            return null;
        }

        public ValueResult<TimeSpan> GetRemainingTime(CardCounterGameContext context, DateTimeOffset now) => _expiresAt - now;

        private ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> ResolveEffect(CardCounterGameContext context, int sourceDigitIndex, int targetDigitIndex)
        {
            var source = context.GetPlayer(_sourceId);
            var target = context.GetPlayer(_targetId);

            if (source is not null && target is not null &&
                source.Pot.Count > 0 && target.Pot.Count > 0)
            {
                int si = sourceDigitIndex >= 0 && sourceDigitIndex < source.Pot.Count
                    ? sourceDigitIndex
                    : source.Pot.Count - 1;
                int ti = targetDigitIndex >= 0 && targetDigitIndex < target.Pot.Count
                    ? targetDigitIndex
                    : target.Pot.Count - 1;

                (source.Pot[si], target.Pot[ti]) = (target.Pot[ti], source.Pot[si]);
                context.Logger.LogInformation(
                    "Skim applied: [{src}] digit[{si}] ↔ [{tgt}] digit[{ti}].",
                    _sourceId, si, _targetId, ti);
            }

            return new PlayerTurnState();
        }
    }
}
