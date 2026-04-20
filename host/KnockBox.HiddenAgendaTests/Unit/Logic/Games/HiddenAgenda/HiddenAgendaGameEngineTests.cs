using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.HiddenAgenda.Tests.Unit.Logic
{
    [TestClass]
    public class HiddenAgendaGameEngineTests
    {
        private Mock<IRandomNumberService> _rngMock = default!;
        private Mock<ILogger<HiddenAgendaGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<HiddenAgendaGameState>> _stateLoggerMock = default!;
        private User _host = default!;
        private HiddenAgendaGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _rngMock = new Mock<IRandomNumberService>();
            _engineLoggerMock = new Mock<ILogger<HiddenAgendaGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<HiddenAgendaGameState>>();
            _host = new User("Host", "host1");

            _engine = new HiddenAgendaGameEngine(
                _rngMock.Object,
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        [TestMethod]
        public async Task CreateStateAsync_WithHost_ReturnsGameState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            var state = (HiddenAgendaGameState)result.Value!;
            Assert.IsNotNull(state);
            Assert.AreSame(_host, state.Host);
            Assert.IsTrue(state.IsJoinable);
        }

        [TestMethod]
        public async Task StartAsync_ValidHostAndState_StartsGame()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (HiddenAgendaGameState)stateResult.Value!;
            
            // Register minimum players (3)
            // Note: Host is NOT a player in the Players list for AbstractGameState
            state.RegisterPlayer(new User("Player 1", "p1"));
            state.RegisterPlayer(new User("Player 2", "p2"));
            state.RegisterPlayer(new User("Player 3", "p3"));

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsFalse(state.IsJoinable);
            Assert.IsNotNull(state.Context);
            Assert.IsNotNull(state.Context.Fsm);
            Assert.IsNotNull(state.BoardGraph);
            Assert.AreEqual(3, state.GamePlayers.Count);
            Assert.AreEqual(3, state.TurnManager.TurnOrder.Count);
            Assert.IsTrue(state.CollectionProgress.Count > 0);
        }

        [TestMethod]
        public async Task StartAsync_InvalidHost_ReturnsError()
        {
            var stateResult = await _engine.CreateStateAsync(_host);
            var state = (HiddenAgendaGameState)stateResult.Value!;
            var nonHost = new User("Not Host", "non-host");

            var result = await _engine.StartAsync(nonHost, state);

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public void PlayerCounts_AreCorrect()
        {
            Assert.AreEqual(3, _engine.MinPlayerCount);
            Assert.AreEqual(6, _engine.MaxPlayerCount);
        }
    }
}
