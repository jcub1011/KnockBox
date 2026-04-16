using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.HiddenAgenda.Tests.Unit.Logic.States
{
    [TestClass]
    public class RoundOverStateTests
    {
        private Mock<IRandomNumberService> _rngMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<HiddenAgendaGameState>> _stateLoggerMock = default!;
        private HiddenAgendaGameState _state = default!;
        private HiddenAgendaGameContext _context = default!;
        private RoundOverState _stateLogic = default!;

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
            _stateLogic = new RoundOverState();

            _state.GamePlayers["p1"] = new HiddenAgendaPlayerState { PlayerId = "p1", RoundScore = 10, CumulativeScore = 5 };
            _state.Config.TotalRounds = 3;
            _state.CurrentRound = 1;
        }

        [TestMethod]
        public void OnEnter_AccumulatesScores()
        {
            _stateLogic.OnEnter(_context);
            
            Assert.AreEqual(15, _state.GamePlayers["p1"].CumulativeScore);
            Assert.AreEqual(GamePhase.RoundOver, _state.Phase);
        }
[TestMethod]
public void StartNextRound_HostOnly_TransitionsToSetup()
{
    _stateLogic.OnEnter(_context);

    // Non-host
    var res1 = _stateLogic.HandleCommand(_context, new StartNextRoundCommand("other"));
    Assert.IsNotNull(res1.Error);

    // Host
    var res2 = _stateLogic.HandleCommand(_context, new StartNextRoundCommand("host1"));
    Assert.IsInstanceOfType(res2.Value, typeof(RoundSetupState));
}

[TestMethod]
public void StartNextRound_AfterFinalRound_TransitionsToMatchOver()
{
    _state.CurrentRound = 3;
    _stateLogic.OnEnter(_context);

    var result = _stateLogic.HandleCommand(_context, new StartNextRoundCommand("host1"));
    Assert.IsInstanceOfType(result.Value, typeof(MatchOverState));
}
}
}
