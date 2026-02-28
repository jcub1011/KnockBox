using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.Navigation.Games.DiceSimulator;
using KnockBox.Services.State.Games.DiceSimulator;
using KnockBox.Services.State.Games.DiceSimulator.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading.Tasks;

namespace KnockBoxTests.Unit.Logic.Games.DiceSimulator
{
    [TestClass]
    public class DiceSimulatorGameEngineTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger<DiceSimulatorGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<DiceSimulatorGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private DiceSimulatorGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            _engineLoggerMock = new Mock<ILogger<DiceSimulatorGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<DiceSimulatorGameState>>();
            _host = new User("Host", "host1");

            _engine = new DiceSimulatorGameEngine(
                _randomMock.Object,
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        [TestMethod]
        public async Task CreateStateAsync_WithHost_ReturnsGameState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            var state = (DiceSimulatorGameState)result.Value!;
            Assert.IsNotNull(state);
            Assert.AreSame(_host, state.Host);
            Assert.IsTrue(state.IsJoinable);
        }

        [TestMethod]
        public async Task CreateStateAsync_NullHost_ReturnsError()
        {
            var result = await _engine.CreateStateAsync(null!);

            Assert.IsTrue((bool)result.IsFailure);
            Assert.IsInstanceOfType(result.Error, typeof(ArgumentNullException));
        }

        [TestMethod]
        public async Task StartAsync_ValidHostAndState_StartsGame()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DiceSimulatorGameState)stateResult.Value!;

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsFalse(state.IsJoinable);
        }

        [TestMethod]
        public async Task StartAsync_InvalidHost_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DiceSimulatorGameState)stateResult.Value!;
            var nonHost = new User("Not Host", "non-host");

            var result = await _engine.StartAsync(nonHost, state);

            Assert.IsTrue((bool)result.IsFailure);
            Assert.IsInstanceOfType(result.Error, typeof(InvalidOperationException));
        }

        [TestMethod]
        public async Task StartAsync_InvalidStateType_ReturnsError()
        {
            // We use a mock or null as invalid cast
            var result = await _engine.StartAsync(_host, null!);
            Assert.IsTrue((bool)result.IsFailure);
            Assert.IsInstanceOfType(result.Error, typeof(InvalidCastException));
        }

        [TestMethod]
        public async Task RollDice_NormalRoll_UpdatesStatsAndHistory()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DiceSimulatorGameState)stateResult.Value!;
            
            // Mock random to return 10
            _randomMock.Setup(r => r.GetRandomInt(1, 21, RandomType.Fast)).Returns(10);

            var action = new DiceRollAction
            {
                DiceCount = 2,
                DiceType = DiceType.D20,
                Modifier = 2,
                Mode = RollMode.Normal
            };

            var user = new User("Player", "p1");

            var result = _engine.RollDice(user, state, action);

            Assert.IsTrue((bool)result.IsSuccess);

            Assert.AreEqual(1, state.RollHistory.Count);
            var roll = state.RollHistory[0];
            Assert.AreEqual(22, roll.Result); // 10 + 10 + 2 = 22
            Assert.AreEqual(2, roll.RawRolls.Length);
            Assert.IsNull(roll.AltRolls);

            var stats = state.PlayerStats["p1"];
            Assert.AreEqual(1, stats.TotalRolls);
            Assert.AreEqual(2, stats.TotalDiceRolled);
            Assert.AreEqual(22, stats.HighestResult);
            Assert.AreEqual(22, stats.CumulativeTotal);
        }

        [TestMethod]
        public async Task RollDice_Advantage_SelectsHighestTotal()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DiceSimulatorGameState)stateResult.Value!;
            
            // Returns 5, then 2. Raw=5, Alt=2. Advantage keeps highest so 5.
            int[] sequence = new[] { 5, 2 }; 
            int callCnt = 0;
            _randomMock.Setup(r => r.GetRandomInt(1, 21, RandomType.Fast))
                       .Returns(() => sequence[callCnt++ % sequence.Length]);

            var action = new DiceRollAction
            {
                DiceCount = 1,
                DiceType = DiceType.D20,
                Modifier = 0,
                Mode = RollMode.Advantage
            };

            var user = new User("Player", "p1");
            var result = _engine.RollDice(user, state, action);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(5, state.RollHistory[0].Result);
            Assert.IsNotNull(state.RollHistory[0].AltRolls);
        }

        [TestMethod]
        public async Task RollDice_Disadvantage_SelectsLowestTotal()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DiceSimulatorGameState)stateResult.Value!;
            
            // Returns 18, then 4. Raw=18, Alt=4. Disadvantage keeps lowest so 4.
            int[] sequence = new[] { 18, 4 }; 
            int callCnt = 0;
            _randomMock.Setup(r => r.GetRandomInt(1, 21, RandomType.Fast))
                       .Returns(() => sequence[callCnt++ % sequence.Length]);

            var action = new DiceRollAction
            {
                DiceCount = 1,
                DiceType = DiceType.D20,
                Modifier = 0,
                Mode = RollMode.Disadvantage
            };

            var user = new User("Player", "p1");
            var result = _engine.RollDice(user, state, action);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(4, state.RollHistory[0].Result);
            Assert.IsNotNull(state.RollHistory[0].AltRolls);
        }

        [TestMethod]
        public async Task RollDice_Nat1AndNat20Counted()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DiceSimulatorGameState)stateResult.Value!;
            
            int callCnt = 0;
            int[] vals = new[] { 20, 1 };
            _randomMock.Setup(r => r.GetRandomInt(1, 21, RandomType.Fast))
                       .Returns(() => vals[callCnt++ % vals.Length]);

            var action = new DiceRollAction { DiceCount = 1, DiceType = DiceType.D20, Modifier = 0, Mode = RollMode.Normal };

            // Roll twenty
            _engine.RollDice(_host, state, action);
            // Roll one
            _engine.RollDice(_host, state, action);

            var stats = state.PlayerStats[_host.Id];
            Assert.AreEqual(1, stats.NatTwentyCount);
            Assert.AreEqual(1, stats.NatOneCount);
        }

        [TestMethod]
        public async Task ClearHistory_Host_Clears()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DiceSimulatorGameState)stateResult.Value!;
            
            _engine.RollDice(_host, state, new DiceRollAction());
            Assert.AreEqual(1, state.RollHistory.Count);

            var result = _engine.ClearHistory(_host, state);
            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(0, state.RollHistory.Count);
            Assert.AreEqual(0, state.PlayerStats.Count);
        }
        
        [TestMethod]
        public async Task ClearHistory_NonHost_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (DiceSimulatorGameState)stateResult.Value!;
            var user = new User("A", "B");
            
            var result = _engine.ClearHistory(user, state);
            Assert.IsTrue((bool)result.IsFailure);
        }
    }
}
