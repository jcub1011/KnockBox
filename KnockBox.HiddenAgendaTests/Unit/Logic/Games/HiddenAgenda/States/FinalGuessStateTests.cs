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
    public class FinalGuessStateTests
    {
        private Mock<IRandomNumberService> _rngMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<HiddenAgendaGameState>> _stateLoggerMock = default!;
        private HiddenAgendaGameState _state = default!;
        private HiddenAgendaGameContext _context = default!;
        private FinalGuessState _stateLogic = default!;

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
            _stateLogic = new FinalGuessState();

            _state.GamePlayers["p1"] = new HiddenAgendaPlayerState { PlayerId = "p1", HasSubmittedGuess = true };
            _state.GamePlayers["p2"] = new HiddenAgendaPlayerState { PlayerId = "p2", HasSubmittedGuess = false };
            _state.GamePlayers["p3"] = new HiddenAgendaPlayerState { PlayerId = "p3", HasSubmittedGuess = false };
        }

        [TestMethod]
        public void OnEnter_WithNonGuessers_SetsPhase()
        {
            var result = _stateLogic.OnEnter(_context);
            
            Assert.IsNull(result.Value);
            Assert.AreEqual(GamePhase.FinalGuess, _state.Phase);
        }

        [TestMethod]
        public void OnEnter_WithNoNonGuessers_TransitionsToReveal()
        {
            foreach (var p in _state.GamePlayers.Values) p.HasSubmittedGuess = true;
            
            var result = _stateLogic.OnEnter(_context);
            
            Assert.IsInstanceOfType(result.Value, typeof(RevealState));
        }

        [TestMethod]
        public void SubmitFinalGuess_AllSubmitted_TransitionsToReveal()
        {
            _stateLogic.OnEnter(_context);
            _state.CurrentTaskPool = TaskPool.AllTasks.Take(30).ToList();
            var guesses = new Dictionary<string, List<string>>
            {
                { "p1", _state.CurrentTaskPool.Take(3).Select(t => t.Id).ToList() },
                { "p3", _state.CurrentTaskPool.Skip(3).Take(3).Select(t => t.Id).ToList() }
            };

            // p2 submits
            var res1 = _stateLogic.HandleCommand(_context, new SubmitFinalGuessCommand("p2", guesses));
            Assert.IsNull(res1.Value);
            Assert.IsTrue(_state.GamePlayers["p2"].HasSubmittedGuess);

            // p3 skips
            var res2 = _stateLogic.HandleCommand(_context, new SkipFinalGuessCommand("p3"));
            Assert.IsInstanceOfType(res2.Value, typeof(RevealState));
            Assert.IsTrue(_state.GamePlayers["p3"].HasSubmittedGuess);
        }

        [TestMethod]
        public void Tick_OnTimeout_TransitionsToReveal()
        {
            _stateLogic.OnEnter(_context);
            var result = _stateLogic.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));
            
            Assert.IsInstanceOfType(result.Value, typeof(RevealState));
            Assert.IsTrue(_state.GamePlayers["p2"].HasSubmittedGuess);
            Assert.IsTrue(_state.GamePlayers["p3"].HasSubmittedGuess);
        }
    }
}
