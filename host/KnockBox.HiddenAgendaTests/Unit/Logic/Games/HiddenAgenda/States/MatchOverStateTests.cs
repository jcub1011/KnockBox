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
    public class MatchOverStateTests
    {
        private Mock<IRandomNumberService> _rngMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<HiddenAgendaGameState>> _stateLoggerMock = default!;
        private HiddenAgendaGameState _state = default!;
        private HiddenAgendaGameContext _context = default!;
        private MatchOverState _stateLogic = default!;

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
            _stateLogic = new MatchOverState();

            _state.GamePlayers["p1"] = new HiddenAgendaPlayerState { PlayerId = "p1", CumulativeScore = 20 };
            _state.GamePlayers["p2"] = new HiddenAgendaPlayerState { PlayerId = "p2", CumulativeScore = 25 };
        }

        [TestMethod]
        public void OnEnter_DeterminesWinner()
        {
            _stateLogic.OnEnter(_context);
            
            Assert.AreEqual("p2", _state.MatchWinner);
            Assert.AreEqual(GamePhase.MatchOver, _state.Phase);
        }
[TestMethod]
public void ReturnToLobby_HostOnly_SetsLobbyPhase()
{
    _stateLogic.OnEnter(_context);

    // Non-host
    var res1 = _stateLogic.HandleCommand(_context, new ReturnToLobbyCommand("other"));
    Assert.IsNotNull(res1.Error);

    // Host
    var res2 = _stateLogic.HandleCommand(_context, new ReturnToLobbyCommand("host1"));
    Assert.IsNull(res2.Value);
    Assert.AreEqual(GamePhase.Lobby, _state.Phase);
}

[TestMethod]
public void PlayAgain_HostOnly_ResetsMatch()
{
    _stateLogic.OnEnter(_context);

    // Host
    var result = _stateLogic.HandleCommand(_context, new PlayAgainCommand("host1"));
    Assert.IsInstanceOfType(result.Value, typeof(RoundSetupState));
    Assert.AreEqual(0, _state.CurrentRound);
    Assert.AreEqual(0, _state.GamePlayers["p1"].CumulativeScore);
    Assert.IsNull(_state.MatchWinner);
}
}
}
