using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.CardCounter.Services.State.Games;

namespace KnockBox.CardCounter.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Entered when the active player draws an operator card while holding an active
    /// Not My Money effect. The player selects a target to receive the operator,
    /// or cancels to apply it to themselves.
    /// </summary>
    public sealed class NotMyMoneyState(string playerId, OperatorCard operatorCard) : ITimedCardCounterGameState
    {
        private readonly string _playerId = playerId;
        private readonly OperatorCard _operatorCard = operatorCard;
        private DateTimeOffset _expiresAt;

        public ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> OnEnter(CardCounterGameContext context)
        {
            _expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(context.Config.NotMyMoneyTimeoutMs);
            context.State.IsNotMyMoneySelecting = true;
            context.State.PendingNotMyMoneyOperator = _operatorCard.Op;
            context.Logger.LogDebug(
                "FSM → NotMyMoneyState: [{id}] redirecting operator [{op}]. Expires {exp}.",
                _playerId, _operatorCard.Op, _expiresAt);
            return null;
        }

        public Result OnExit(CardCounterGameContext context) => Result.Success;

        public ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> HandleCommand(CardCounterGameContext context, CardCounterCommand command)
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
                }

                // Transition to reaction state where target can block
                context.State.IsNotMyMoneySelecting = false;
                context.State.PendingNotMyMoneyOperator = null;

                return new WaitingForReactionState(_playerId, target.PlayerId, new ActionCard(ActionType.NotMyMoney), _operatorCard);
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

                context.Logger.LogDebug(
                    "NotMyMoney: [{id}] cancelled; operator applied to self.", _playerId);
                return FinishTurn(context);
            }

            return null;
        }

        public ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> Tick(CardCounterGameContext context, DateTimeOffset now)
        {
            if (now >= _expiresAt)
            {
                context.Logger.LogDebug(
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

        public ValueResult<TimeSpan> GetRemainingTime(CardCounterGameContext context, DateTimeOffset now) => _expiresAt - now;

        private ValueResult<IGameState<CardCounterGameContext, CardCounterCommand>?> FinishTurn(CardCounterGameContext context)
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
