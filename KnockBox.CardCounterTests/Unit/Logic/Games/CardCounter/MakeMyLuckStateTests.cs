using KnockBox.Services.Logic.Games.CardCounter.FSM;
using KnockBox.Services.Logic.Games.CardCounter.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBoxTests.Unit.Logic.Games.CardCounter
{
    [TestClass]
    public class MakeMyLuckStateTests
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
        public void SubmitReorder_ValidReorder_RearrangesTopCardsInShoe()
        {
            var player = AddPlayer("p1", "Player 1");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            // Shoe (top to bottom): card3, card2, card1
            var card1 = new NumberCard(1);
            var card2 = new NumberCard(2);
            var card3 = new NumberCard(3);
            _state.CurrentShoe.Push(card1);
            _state.CurrentShoe.Push(card2);
            _state.CurrentShoe.Push(card3); // card3 is on top

            // Reveal = [card3, card2, card1] (Take from top)
            player.PrivateReveal = [card3, card2, card1];

            var fsmState = new MakeMyLuckState("p1");
            fsmState.OnEnter(_context);

            // Player wants order: [card1, card2, card3] (indices 2, 1, 0 of the reveal)
            var next = fsmState.HandleCommand(_context, new SubmitReorderCommand("p1", [2, 1, 0]));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));

            // New top of shoe should be card1 (the reordered[0])
            Assert.AreEqual(card1, _state.CurrentShoe.Peek(), "New top of shoe should be the first element of the submitted order.");
        }

        [TestMethod]
        public void SubmitReorder_WrongPlayer_IsNoOp()
        {
            var player = AddPlayer("p1", "Player 1");
            var other = AddPlayer("p2", "Player 2");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var card = new NumberCard(5);
            _state.CurrentShoe.Push(card);
            player.PrivateReveal = [card];

            var fsmState = new MakeMyLuckState("p1");
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new SubmitReorderCommand("p2", [0]));

            Assert.IsNull(next.Value, "Commands from the wrong player should be ignored.");
            Assert.AreEqual(card, _state.CurrentShoe.Peek(), "Shoe should be unchanged.");
        }

        [TestMethod]
        public void SubmitReorder_DuplicateIndices_IsNoOp()
        {
            var player = AddPlayer("p1", "Player 1");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var card1 = new NumberCard(1);
            var card2 = new NumberCard(2);
            _state.CurrentShoe.Push(card1);
            _state.CurrentShoe.Push(card2);
            player.PrivateReveal = [card2, card1];

            var fsmState = new MakeMyLuckState("p1");
            fsmState.OnEnter(_context);

            // Duplicate index 0
            var next = fsmState.HandleCommand(_context, new SubmitReorderCommand("p1", [0, 0]));

            Assert.IsNull(next.Value, "Duplicate indices should be rejected.");
        }

        [TestMethod]
        public void SubmitReorder_OutOfRangeIndex_IsNoOp()
        {
            var player = AddPlayer("p1", "Player 1");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var card = new NumberCard(3);
            _state.CurrentShoe.Push(card);
            player.PrivateReveal = [card];

            var fsmState = new MakeMyLuckState("p1");
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new SubmitReorderCommand("p1", [99]));

            Assert.IsNull(next.Value, "Out-of-range index should be rejected.");
        }

        [TestMethod]
        public void SubmitReorder_WrongNumberOfIndices_IsNoOp()
        {
            var player = AddPlayer("p1", "Player 1");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var card1 = new NumberCard(1);
            var card2 = new NumberCard(2);
            _state.CurrentShoe.Push(card1);
            _state.CurrentShoe.Push(card2);
            player.PrivateReveal = [card2, card1];

            var fsmState = new MakeMyLuckState("p1");
            fsmState.OnEnter(_context);

            // Wrong count: only 1 index for 2-card reveal
            var next = fsmState.HandleCommand(_context, new SubmitReorderCommand("p1", [0]));

            Assert.IsNull(next.Value, "Wrong number of indices should be rejected.");
        }

        [TestMethod]
        public void SubmitReorder_ClearsPrivateReveal()
        {
            var player = AddPlayer("p1", "Player 1");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var card = new NumberCard(5);
            _state.CurrentShoe.Push(card);
            player.PrivateReveal = [card];

            var fsmState = new MakeMyLuckState("p1");
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new SubmitReorderCommand("p1", [0]));

            Assert.IsNull(player.PrivateReveal, "PrivateReveal should be cleared after reorder.");
        }

        [TestMethod]
        public void SubmitReorder_TransitionsToPlayerTurnState()
        {
            var player = AddPlayer("p1", "Player 1");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var card = new NumberCard(7);
            _state.CurrentShoe.Push(card);
            player.PrivateReveal = [card];

            var fsmState = new MakeMyLuckState("p1");
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new SubmitReorderCommand("p1", [0]));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
        }

        [TestMethod]
        public void NonSubmitReorderCommand_IsIgnored()
        {
            var player = AddPlayer("p1", "Player 1");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var card = new NumberCard(7);
            _state.CurrentShoe.Push(card);
            player.PrivateReveal = [card];

            var fsmState = new MakeMyLuckState("p1");
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new DrawCardCommand("p1"));

            Assert.IsNull(next.Value);
        }
    }
}
