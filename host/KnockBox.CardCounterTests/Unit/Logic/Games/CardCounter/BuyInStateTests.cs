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
    public class BuyInStateTests
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
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<CardCounterGameState>>();

            var host = new User("Host", "host-id");
            _state = new CardCounterGameState(host, _stateLoggerMock.Object);
            _context = new CardCounterGameContext(_state, _randomMock.Object, _loggerMock.Object);
        }

        private PlayerState AddPlayer(string id, string name, int buyInRoll = 3)
        {
            var player = new PlayerState { PlayerId = id, DisplayName = name, BuyInRoll = buyInRoll };
            _state.GamePlayers[id] = player;
            _state.TurnManager.TurnOrder.Add(id);
            return player;
        }

        [TestMethod]
        public void OnEnter_SetsBuyInPhase()
        {
            var fsmState = new BuyInState();
            fsmState.OnEnter(_context);

            Assert.AreEqual(GamePhase.BuyIn, _state.Phase);
        }

        [TestMethod]
        public void SetBuyIn_Positive_SetsPositiveBalance()
        {
            var player = AddPlayer("p1", "Player 1", buyInRoll: 3);

            var fsmState = new BuyInState();
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new SetBuyInCommand("p1", IsNegative: false));

            Assert.AreEqual(24.0, player.Balance); // 3 * 8
            Assert.IsTrue(player.HasSetBuyIn);
        }

        [TestMethod]
        public void SetBuyIn_Negative_SetsNegativeBalance()
        {
            var player = AddPlayer("p1", "Player 1", buyInRoll: 5);

            var fsmState = new BuyInState();
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new SetBuyInCommand("p1", IsNegative: true));

            Assert.AreEqual(-40.0, player.Balance); // -(5 * 8)
            Assert.IsTrue(player.HasSetBuyIn);
        }

        [TestMethod]
        public void SetBuyIn_AlreadySet_IsNoOp()
        {
            var player = AddPlayer("p1", "Player 1", buyInRoll: 3);
            player.HasSetBuyIn = true;
            player.Balance = 100.0;

            var fsmState = new BuyInState();
            fsmState.OnEnter(_context);
            fsmState.HandleCommand(_context, new SetBuyInCommand("p1", IsNegative: false));

            Assert.AreEqual(100.0, player.Balance, "Balance should not change if buy-in already set.");
        }

        [TestMethod]
        public void SetBuyIn_UnknownPlayer_IsNoOp()
        {
            var fsmState = new BuyInState();
            fsmState.OnEnter(_context);
            var result = fsmState.HandleCommand(_context, new SetBuyInCommand("unknown-player", IsNegative: false));

            Assert.IsNull(result.Value, "Unknown player command should return null (stay in BuyInState).");
        }

        [TestMethod]
        public void SetBuyIn_AllPlayersSet_TransitionsToRoundEndState()
        {
            var p1 = AddPlayer("p1", "Player 1", buyInRoll: 2);
            var p2 = AddPlayer("p2", "Player 2", buyInRoll: 4);

            // p2 sets buy-in first
            p2.HasSetBuyIn = true;
            p2.Balance = 32.0;

            // Supply shoe cards so RoundEndState doesn't go directly to GameOver
            _state.MainDeck.Push(new NumberCard(5));
            _state.MainDeck.Push(new NumberCard(6));

            var fsmState = new BuyInState();
            fsmState.OnEnter(_context);
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), RandomType.Secure)).Returns(0);

            // p1 sets buy-in — now all players have set buy-in
            var next = fsmState.HandleCommand(_context, new SetBuyInCommand("p1", IsNegative: false));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(RoundEndState));
        }

        [TestMethod]
        public void SetBuyIn_NotLastPlayer_StaysInBuyInState()
        {
            AddPlayer("p1", "Player 1", buyInRoll: 2);
            AddPlayer("p2", "Player 2", buyInRoll: 4);

            var fsmState = new BuyInState();
            fsmState.OnEnter(_context);

            // Only p1 sets buy-in; p2 has not yet
            var next = fsmState.HandleCommand(_context, new SetBuyInCommand("p1", IsNegative: false));

            Assert.IsNull(next.Value, "Should stay in BuyInState while not all players have set buy-in.");
        }

        [TestMethod]
        public void SetBuyIn_IgnoresNonSetBuyInCommands()
        {
            AddPlayer("p1", "Player 1");

            var fsmState = new BuyInState();
            fsmState.OnEnter(_context);

            // DrawCardCommand should be ignored in BuyIn state
            var next = fsmState.HandleCommand(_context, new DrawCardCommand("p1"));

            Assert.IsNull(next.Value);
        }
    }
}
