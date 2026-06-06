using KnockBox.CardCounter.Services.Logic.Games.FSM;
using KnockBox.CardCounter.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.CardCounter.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.CardCounter.Tests.Unit.Logic.Games.CardCounter
{
    [TestClass]
    public class NotMyMoneyStateTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<CardCounterGameState>> _stateLoggerMock = default!;
        private CardCounterGameState _state = default!;
        private CardCounterGameContext _context = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<CardCounterGameState>>();

            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new CardCounterGameState(host, _stateLoggerMock.Object);
            _context = new CardCounterGameContext(_state, _randomMock.Object, _loggerMock.Object);
        }

        private PlayerState AddPlayer(string name)
        {
            var id = Guid.NewGuid();
            var player = new PlayerState { PlayerId = id, DisplayName = name };
            _state.GamePlayers[id] = player;
            _state.TurnManager.TurnOrder.Add(id);
            return player;
        }

        [TestMethod]
        public void OnEnter_SetsIsNotMyMoneySelectingAndPendingOperator()
        {
            var player = AddPlayer("Player 1");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var operatorCard = new OperatorCard(Operator.Add);
            var fsmState = new NotMyMoneyState(player.PlayerId, operatorCard);
            fsmState.OnEnter(_context);

            Assert.IsTrue(_state.IsNotMyMoneySelecting);
            Assert.AreEqual(Operator.Add, _state.PendingNotMyMoneyOperator);
        }

        [TestMethod]
        public void SelectTarget_ValidTarget_TransitionsToWaitingForReactionState()
        {
            var player = AddPlayer("Player 1");
            var target = AddPlayer("Player 2");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            player.ActionHand.Add(new ActionCard(ActionType.NotMyMoney));
            _state.CurrentShoe.Push(new NumberCard(1)); // keep shoe non-empty

            var operatorCard = new OperatorCard(Operator.Multiply);
            var fsmState = new NotMyMoneyState(player.PlayerId, operatorCard);
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new NotMyMoneySelectTargetCommand(player.PlayerId, target.PlayerId));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(WaitingForReactionState));
        }

        [TestMethod]
        public void SelectTarget_ValidTarget_ConsumesNotMyMoneyCard()
        {
            var player = AddPlayer("Player 1");
            var target = AddPlayer("Player 2");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            player.ActionHand.Add(new ActionCard(ActionType.NotMyMoney));

            var fsmState = new NotMyMoneyState(player.PlayerId, new OperatorCard(Operator.Add));
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new NotMyMoneySelectTargetCommand(player.PlayerId, target.PlayerId));

            Assert.IsEmpty(player.ActionHand, "Not My Money card should be consumed when redirecting.");
        }

        [TestMethod]
        public void SelectTarget_ClearsIsNotMyMoneySelecting()
        {
            var player = AddPlayer("Player 1");
            var target = AddPlayer("Player 2");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            player.ActionHand.Add(new ActionCard(ActionType.NotMyMoney));

            var fsmState = new NotMyMoneyState(player.PlayerId, new OperatorCard(Operator.Add));
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new NotMyMoneySelectTargetCommand(player.PlayerId, target.PlayerId));

            Assert.IsFalse(_state.IsNotMyMoneySelecting, "IsNotMyMoneySelecting should be cleared after selecting a target.");
        }

        [TestMethod]
        public void SelectTarget_UnknownTarget_IsNoOp()
        {
            var player = AddPlayer("Player 1");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var fsmState = new NotMyMoneyState(player.PlayerId, new OperatorCard(Operator.Add));
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new NotMyMoneySelectTargetCommand(player.PlayerId, Guid.NewGuid()));

            Assert.IsNull(next.Value, "Selecting an unknown target should be rejected.");
        }

        [TestMethod]
        public void Cancel_AppliesOperatorToSelf()
        {
            var player = AddPlayer("Player 1");
            AddPlayer("Player 2"); // need at least 2 players for turn advance
            _state.TurnManager.SetCurrentPlayerIndex(0);
            player.Balance = 100;
            player.Pot.Add(5); // pot = 5
            _state.CurrentShoe.Push(new NumberCard(1)); // keep shoe non-empty

            var operatorCard = new OperatorCard(Operator.Add);
            var fsmState = new NotMyMoneyState(player.PlayerId, operatorCard);
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new NotMyMoneyCancelCommand(player.PlayerId));

            Assert.AreEqual(105.0, player.Balance, "Cancel should apply the operator to the player themselves.");
        }

        [TestMethod]
        public void Cancel_ClearsIsNotMyMoneySelecting()
        {
            var player = AddPlayer("Player 1");
            AddPlayer("Player 2");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1));

            var fsmState = new NotMyMoneyState(player.PlayerId, new OperatorCard(Operator.Add));
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new NotMyMoneyCancelCommand(player.PlayerId));

            Assert.IsFalse(_state.IsNotMyMoneySelecting);
            Assert.IsNull(_state.PendingNotMyMoneyOperator);
        }

        [TestMethod]
        public void Cancel_AdvancesTurn()
        {
            var player = AddPlayer("Player 1");
            AddPlayer("Player 2");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1)); // keep shoe non-empty

            var fsmState = new NotMyMoneyState(player.PlayerId, new OperatorCard(Operator.Add));
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new NotMyMoneyCancelCommand(player.PlayerId));

            Assert.AreEqual(1, _state.TurnManager.CurrentPlayerIndex, "Turn should advance to p2 after cancel.");
        }

        [TestMethod]
        public void Cancel_TransitionsToPlayerTurnState()
        {
            var player = AddPlayer("Player 1");
            AddPlayer("Player 2");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1));

            var fsmState = new NotMyMoneyState(player.PlayerId, new OperatorCard(Operator.Subtract));
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new NotMyMoneyCancelCommand(player.PlayerId));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
        }

        [TestMethod]
        public void Cancel_WhenShoeEmpty_TransitionsToRoundEndState()
        {
            var player = AddPlayer("Player 1");
            AddPlayer("Player 2");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            // Shoe is empty

            var fsmState = new NotMyMoneyState(player.PlayerId, new OperatorCard(Operator.Add));
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new NotMyMoneyCancelCommand(player.PlayerId));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(RoundEndState));
        }

        [TestMethod]
        public void UnrelatedPlayer_CommandIsIgnored()
        {
            var player = AddPlayer("Player 1");
            var other = AddPlayer("Other");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var fsmState = new NotMyMoneyState(player.PlayerId, new OperatorCard(Operator.Add));
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new NotMyMoneyCancelCommand(other.PlayerId));

            Assert.IsNull(next.Value, "Commands from other players should be ignored.");
        }

        [TestMethod]
        public void SelectTarget_RecordsLastPlayedAction()
        {
            var player = AddPlayer("Player 1");
            var target = AddPlayer("Player 2");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            player.ActionHand.Add(new ActionCard(ActionType.NotMyMoney));

            var fsmState = new NotMyMoneyState(player.PlayerId, new OperatorCard(Operator.Add));
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new NotMyMoneySelectTargetCommand(player.PlayerId, target.PlayerId));

            Assert.IsNotNull(_state.LastPlayedAction);
            Assert.AreEqual(ActionType.NotMyMoney, _state.LastPlayedAction.Action);
            Assert.AreEqual(player.PlayerId, _state.LastPlayedAction.PlayerId);
            Assert.AreEqual(target.PlayerId, _state.LastPlayedAction.TargetId);
        }
    }
}
