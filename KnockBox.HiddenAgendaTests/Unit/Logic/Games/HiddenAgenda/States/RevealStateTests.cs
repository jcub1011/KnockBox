using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.HiddenAgenda.Tests.Unit.Logic.States
{
    [TestClass]
    public class RevealStateTests
    {
        private Mock<IRandomNumberService> _rngMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<HiddenAgendaGameState>> _stateLoggerMock = default!;
        private HiddenAgendaGameState _state = default!;
        private HiddenAgendaGameContext _context = default!;
        private RevealState _stateLogic = default!;

        [TestInitialize]
        public void Setup()
        {
            _rngMock = new Mock<IRandomNumberService>();
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<HiddenAgendaGameState>>();
            
            var host = new User("Host", "host1");
            _state = new HiddenAgendaGameState(host, _stateLoggerMock.Object);
            _state.BoardGraph = BoardDefinitions.CreateGrandCircuit();
            
            _context = new HiddenAgendaGameContext(_state, _rngMock.Object, _loggerMock.Object);
            _stateLogic = new RevealState();

            _state.GamePlayers["p1"] = new HiddenAgendaPlayerState { PlayerId = "p1", DisplayName = "P1" };
        }

        [TestMethod]
        public void OnEnter_CalculatesScoreAndSetsPhase()
        {
            var result = _stateLogic.OnEnter(_context);
            
            Assert.IsNull(result.Value);
            Assert.AreEqual(GamePhase.Reveal, _state.Phase);
            Assert.AreEqual(1, _state.RoundResults.Count);
        }

        [TestMethod]
        public void Tick_OnTimeout_TransitionsToRoundOver()
        {
            _stateLogic.OnEnter(_context);
            var result = _stateLogic.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));
            
            Assert.IsInstanceOfType(result.Value, typeof(RoundOverState));
        }
    }
}
