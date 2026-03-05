using KnockBox.Services.State.Games.CardCounter;

namespace KnockBox.Services.Logic.Games.CardCounter.FSM.States
{
    /// <summary>
    /// Entered when the active player draws an operator card while holding an active
    /// Not My Money effect. The player selects a target to receive the operator,
    /// or cancels to apply it to themselves.
    /// </summary>
    public sealed class NotMyMoneyState : ICardCounterGameState
    {
        private readonly string _playerId;
        private readonly OperatorCard _operatorCard;
        private DateTimeOffset _expiresAt;

        public NotMyMoneyState(string playerId, OperatorCard operatorCard)
        {
            _playerId = playerId;
            _operatorCard = operatorCard;
        }

        public void OnEnter(CardCounterGameContext context)
        {
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.Config.ActionResponseTimeoutMs);
            context.State.IsNotMyMoneySelecting = true;
            context.State.PendingNotMyMoneyOperator = _operatorCard.Op;
            context.Logger.LogInformation(
                "FSM → NotMyMoneyState: [{id}] redirecting operator [{op}]. Expires {exp}.",
                _playerId, _operatorCard.Op, _expiresAt);
        }

        public ICardCounterGameState? HandleCommand(CardCounterGameContext context, CardCounterCommand command)
        {
            if (command is NotMyMoneySelectTargetCommand selectCmd && selectCmd.PlayerId == _playerId)
            {
                var target = context.GetPlayer(selectCmd.TargetPlayerId);
                if (target is null)
                {
                    context.Logger.LogWarning(
                        "NotMyMoney: target [{id}] not found.", selectCmd.TargetPlayerId);
                    return null;
                }

                // Apply the operator to the target instead of the drawer
                var drawer = context.GetPlayer(_playerId);
                if (drawer is not null)
                {
                    var cardIndex = drawer.ActionHand.FindIndex(c => c.Action == ActionType.NotMyMoney);
                    if (cardIndex != -1)
                    {
                        var card = drawer.ActionHand[cardIndex];
                        drawer.ActionHand.RemoveAt(cardIndex);
                        context.RecordActionCardPlay(drawer, card);

                        context.State.LastPlayedAction = new LastPlayedActionInfo(
                            _playerId,
                            drawer.DisplayName,
                            card.Action,
                            target.PlayerId,
                            target.DisplayName);
                    }
                    context.RecordRedirectedDraw(drawer, target, _operatorCard);
                }

                context.ApplyOperatorCard(target, _operatorCard);
                context.Logger.LogInformation(
                    "NotMyMoney: operator [{op}] redirected from [{src}] to [{tgt}].",
                    _operatorCard.Op, _playerId, selectCmd.TargetPlayerId);

                return FinishTurn(context);
            }

            if (command is NotMyMoneyCancelCommand cancelCmd && cancelCmd.PlayerId == _playerId)
            {
                // Player cancelled — apply operator to themselves
                var player = context.GetPlayer(_playerId);
                if (player is not null)
                {
                    context.RecordDraw(player, _operatorCard);
                    context.ApplyOperatorCard(player, _operatorCard);
                }

                context.Logger.LogInformation(
                    "NotMyMoney: [{id}] cancelled; operator applied to self.", _playerId);
                return FinishTurn(context);
            }

            return null;
        }

        public ICardCounterGameState? Tick(CardCounterGameContext context, DateTimeOffset now)
        {
            if (now >= _expiresAt)
            {
                context.Logger.LogInformation(
                    "NotMyMoney: timeout; applying operator to self for [{id}].", _playerId);
                var player = context.GetPlayer(_playerId);
                if (player is not null)
                {
                    context.RecordDraw(player, _operatorCard);
                    context.ApplyOperatorCard(player, _operatorCard);
                }
                return FinishTurn(context);
            }
            return null;
        }

        private ICardCounterGameState FinishTurn(CardCounterGameContext context)
        {
            context.State.IsNotMyMoneySelecting = false;
            context.State.PendingNotMyMoneyOperator = null;
            context.AdvanceTurn();

            if (context.CurrentShoe.Count == 0)
                return new RoundEndState();

            return new PlayerTurnState();
        }
    }
}
