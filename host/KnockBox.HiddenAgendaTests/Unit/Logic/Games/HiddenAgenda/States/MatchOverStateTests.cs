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
            
            var host = UserFactory.Create("Host", Guid.Parse("00000000-0000-0000-0000-000000000001"));
            _state = new HiddenAgendaGameState(host, _stateLoggerMock.Object);
            _state.BoardGraph = BoardDefinitions.CreateGrandCircuit();
            
            _context = new HiddenAgendaGameContext(_state, _rngMock.Object, _loggerMock.Object);
            _stateLogic = new MatchOverState();

            _state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")] = new HiddenAgendaPlayerState { PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111"), CumulativeScore = 20 };
            _state.GamePlayers[Guid.Parse("22222222-2222-2222-2222-222222222222")] = new HiddenAgendaPlayerState { PlayerId = Guid.Parse("22222222-2222-2222-2222-222222222222"), CumulativeScore = 25 };
        }

        [TestMethod]
        public void OnEnter_DeterminesWinner()
        {
            _stateLogic.OnEnter(_context);
            
            Assert.AreEqual(Guid.Parse("22222222-2222-2222-2222-222222222222"), _state.MatchWinner);
            Assert.AreEqual(GamePhase.MatchOver, _state.Phase);
        }
[TestMethod]
public void ReturnToLobby_HostOnly_SetsLobbyPhase()
{
    _stateLogic.OnEnter(_context);

    // Non-host
    var res1 = _stateLogic.HandleCommand(_context, new ReturnToLobbyCommand(Guid.NewGuid()));
    Assert.IsNotNull(res1.Error);

    // Host — HandleCommand calls SetJoinable, which debug-asserts the execute
    // lock is held. In production the engine dispatches commands via
    // state.Execute(() => fsm.HandleCommand(...)); mirror that here.
    _state.Execute(() =>
    {
        var res2 = _stateLogic.HandleCommand(_context, new ReturnToLobbyCommand(Guid.Parse("00000000-0000-0000-0000-000000000001")));
        Assert.IsNull(res2.Value);
    });
    Assert.AreEqual(GamePhase.Lobby, _state.Phase);
}

[TestMethod]
public void PlayAgain_HostOnly_ResetsMatch()
{
    _stateLogic.OnEnter(_context);

    // Host
    var result = _stateLogic.HandleCommand(_context, new PlayAgainCommand(Guid.Parse("00000000-0000-0000-0000-000000000001")));
    Assert.IsInstanceOfType(result.Value, typeof(RoundSetupState));
    Assert.AreEqual(0, _state.CurrentRound);
    Assert.AreEqual(0, _state.GamePlayers[Guid.Parse("11111111-1111-1111-1111-111111111111")].CumulativeScore);
    Assert.IsNull(_state.MatchWinner);
}
}
}
