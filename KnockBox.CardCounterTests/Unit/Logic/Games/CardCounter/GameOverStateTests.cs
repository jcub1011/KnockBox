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
    public class GameOverStateTests
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

        // ── OnEnter ───────────────────────────────────────────────────────────

        [TestMethod]
        public void OnEnter_SetsGamePhaseToGameOver()
        {
            var gameOver = new GameOverState();

            gameOver.OnEnter(_context);

            Assert.AreEqual(GamePhase.GameOver, _state.Phase);
        }

        [TestMethod]
        public void OnEnter_PositiveBalance_AddsPotValueToBalance()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.Balance = 10;
            p1.Pot.AddRange([5, 3]); // pot = 53

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.AreEqual(63, p1.Balance, "Pot value 53 should be added to positive balance 10.");
        }

        [TestMethod]
        public void OnEnter_ZeroBalance_AddsPotValueToBalance()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.Balance = 0;
            p1.Pot.AddRange([2, 0]); // pot = 20

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.AreEqual(20, p1.Balance, "Pot value 20 should be added to zero balance.");
        }

        [TestMethod]
        public void OnEnter_NegativeBalance_SubtractsPotValueFromBalance()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.Balance = -10;
            p1.Pot.AddRange([5, 3]); // pot = 53

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.AreEqual(-63, p1.Balance, "Pot value 53 should be subtracted from negative balance -10.");
        }

        [TestMethod]
        public void OnEnter_EmptyPot_DoesNotChangeBalance()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.Balance = 42;
            // Pot is empty

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.AreEqual(42, p1.Balance, "Empty pot should leave balance unchanged.");
        }

        [TestMethod]
        public void OnEnter_ClearsPotAfterApplying()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.Balance = 5;
            p1.Pot.AddRange([1, 2]); // pot = 12

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.AreEqual(0, p1.Pot.Count, "Pot should be cleared after applying to balance.");
        }

        [TestMethod]
        public void OnEnter_MultiplePlayers_AppliesEachPotIndependently()
        {
            var p1 = AddPlayer("p1", "Player 1");
            p1.Balance = 100;
            p1.Pot.AddRange([2, 5]); // pot = 25 → balance + 25 = 125

            var p2 = AddPlayer("p2", "Player 2");
            p2.Balance = -50;
            p2.Pot.AddRange([1, 0]); // pot = 10 → balance - 10 = -60

            var p3 = AddPlayer("p3", "Player 3");
            p3.Balance = 7;
            // empty pot → balance unchanged

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.AreEqual(125, p1.Balance, "Player 1 (positive balance) should have pot added.");
            Assert.AreEqual(-60, p2.Balance, "Player 2 (negative balance) should have pot subtracted.");
            Assert.AreEqual(7, p3.Balance, "Player 3 (empty pot) should have balance unchanged.");
        }
    }
}
