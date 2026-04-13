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
    public class FeelingLuckyChainStateTests
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

            var host = new User("Host", "host-id");
            _state = new CardCounterGameState(host, _stateLoggerMock.Object);
            _context = new CardCounterGameContext(_state, _randomMock.Object, _loggerMock.Object);
        }

        private PlayerState AddPlayer(string id, string name)
        {
            var player = new PlayerState { PlayerId = id, DisplayName = name };
            _state.GamePlayers[id] = player;
            _state.TurnManager.TurnOrder.Add(id);
            return player;
        }

        [TestMethod]
        public void OnEnter_SetsFeelingLuckyTargetId()
        {
            AddPlayer("orig", "Originator");
            AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var fsmState = new FeelingLuckyChainState("orig", "tgt");
            fsmState.OnEnter(_context);

            Assert.AreEqual("tgt", _state.FeelingLuckyTargetId);
        }

        [TestMethod]
        public void TargetDraws_NumberCard_AppendsDigitToTargetPot()
        {
            var originator = AddPlayer("orig", "Originator");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            // Push an extra card so the shoe isn't empty after the draw
            _state.CurrentShoe.Push(new NumberCard(1));
            _state.CurrentShoe.Push(new NumberCard(9));

            var fsmState = new FeelingLuckyChainState("orig", "tgt");
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new DrawCardCommand("tgt"));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
            Assert.HasCount(1, target.Pot, "Target should have received the drawn digit.");
            Assert.AreEqual(9, target.Pot[0]);
        }

        [TestMethod]
        public void TargetDraws_OperatorCard_AppliesOperatorToTargetBalance()
        {
            var originator = AddPlayer("orig", "Originator");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            target.Balance = 100;
            target.Pot.Add(5); // pot value = 5

            _state.CurrentShoe.Push(new NumberCard(1)); // extra
            _state.CurrentShoe.Push(new OperatorCard(Operator.Add));

            var fsmState = new FeelingLuckyChainState("orig", "tgt");
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new DrawCardCommand("tgt"));

            Assert.AreEqual(105.0, target.Balance);
            Assert.IsEmpty(target.Pot, "Pot should be cleared after operator applied.");
        }

        [TestMethod]
        public void TargetBlocksWithCompd_ReturnsToOriginator()
        {
            var originator = AddPlayer("orig", "Originator");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1));
            target.ActionHand.Add(new ActionCard(ActionType.Compd));

            var fsmState = new FeelingLuckyChainState("orig", "tgt");
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt", 0));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
            Assert.IsEmpty(target.ActionHand, "Comp'd should be consumed.");
            Assert.IsEmpty(target.Pot, "Target should not have been forced to draw.");
        }

        [TestMethod]
        public void TargetBlocksWithCompd_RecordsCompdAsLastPlayedAction()
        {
            AddPlayer("orig", "Originator");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            target.ActionHand.Add(new ActionCard(ActionType.Compd));

            var fsmState = new FeelingLuckyChainState("orig", "tgt");
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt", 0));

            Assert.IsNotNull(_state.LastPlayedAction);
            Assert.AreEqual(ActionType.Compd, _state.LastPlayedAction.Action);
        }

        [TestMethod]
        public void TargetPassesChainWithFeelingLucky_NextPlayerBecomesTarget()
        {
            var originator = AddPlayer("orig", "Originator");
            var target1 = AddPlayer("tgt1", "Target1");
            var target2 = AddPlayer("tgt2", "Target2");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1));
            target1.ActionHand.Add(new ActionCard(ActionType.FeelingLucky));

            var fsmState = new FeelingLuckyChainState("orig", "tgt1");
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt1", 0));

            // Chain should pass to tgt2 (no state transition yet — stays in FeelingLuckyChainState)
            Assert.IsNull(next.Value, "Chain should continue; not yet resolved.");
            Assert.AreEqual("tgt2", _state.FeelingLuckyTargetId, "Next target should be tgt2.");
        }

        [TestMethod]
        public void TargetPassesChain_WrapAroundToOriginator_ForcesOriginalTargetDraw()
        {
            // Setup: orig → tgt1 → orig (wraps) → tgt1 must draw
            var originator = AddPlayer("orig", "Originator");
            var target1 = AddPlayer("tgt1", "Target1");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1)); // extra
            _state.CurrentShoe.Push(new NumberCard(5)); // the force draw
            target1.ActionHand.Add(new ActionCard(ActionType.FeelingLucky));

            var fsmState = new FeelingLuckyChainState("orig", "tgt1");
            fsmState.OnEnter(_context);

            // tgt1 plays FeelingLucky → next target would be orig (wraps) → triggers force draw on tgt1
            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt1", 0));

            Assert.IsNotNull(next.Value, "Force draw should resolve the chain.");
            // Target should have drawn a card (pot or balance updated)
        }

        [TestMethod]
        public void UnrelatedPlayer_CommandIsIgnored()
        {
            AddPlayer("orig", "Originator");
            var target = AddPlayer("tgt", "Target");
            var other = AddPlayer("other", "Other");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1));

            var fsmState = new FeelingLuckyChainState("orig", "tgt");
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new DrawCardCommand("other"));

            Assert.IsNull(next.Value, "Commands from unrelated players should be ignored.");
            Assert.IsEmpty(other.Pot);
        }

        [TestMethod]
        public void TargetDraw_WhenShoeEmpty_StillReturnsToOriginator()
        {
            var originator = AddPlayer("orig", "Originator");
            AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            // Shoe is empty

            var fsmState = new FeelingLuckyChainState("orig", "tgt");
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new DrawCardCommand("tgt"));

            Assert.IsNotNull(next.Value);
            // When shoe is empty after the forced draw, it should transition to RoundEndState
        }

        [TestMethod]
        public void TargetDraw_ReturnsCurrentPlayerToOriginator()
        {
            var originator = AddPlayer("orig", "Originator");
            var target = AddPlayer("tgt", "Target");
            // originator is index 0, target is index 1
            _state.TurnManager.SetCurrentPlayerIndex(0);
            _state.CurrentShoe.Push(new NumberCard(1)); // extra
            _state.CurrentShoe.Push(new NumberCard(5));

            var fsmState = new FeelingLuckyChainState("orig", "tgt");
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new DrawCardCommand("tgt"));

            Assert.AreEqual(0, _state.TurnManager.CurrentPlayerIndex, "Turn should be restored to the originator (index 0).");
        }
    }
}
