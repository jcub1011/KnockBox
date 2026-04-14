using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM;
using KnockBox.HiddenAgenda.Services.Logic.Games.FSM.States;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.HiddenAgendaTests.Unit.Logic.Games.HiddenAgenda.States
{
    [TestClass]
    public class GuessPhaseStateTests
    {
        private Mock<IRandomNumberService> _rng = default!;
        private Mock<ILogger> _logger = default!;
        private Mock<ILogger<HiddenAgendaGameState>> _stateLogger = default!;
        private HiddenAgendaGameState _state = default!;
        private HiddenAgendaGameContext _context = default!;

        [TestInitialize]
        public void Setup()
        {
            _rng = new Mock<IRandomNumberService>();
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            
            _logger = new Mock<ILogger>();
            _stateLogger = new Mock<ILogger<HiddenAgendaGameState>>();

            var host = new User("Host", "host-id");
            _state = new HiddenAgendaGameState(host, _stateLogger.Object);
            _state.BoardGraph = BoardDefinitions.CreateGrandCircuit();
            _context = new HiddenAgendaGameContext(_state, _rng.Object, _logger.Object);

            for (int i = 0; i < 4; i++)
            {
                var pid = $"p{i}";
                _state.GamePlayers[pid] = new HiddenAgendaPlayerState
                {
                    PlayerId = pid,
                    DisplayName = $"Player {i}",
                    CurrentSpaceId = 0
                };
            }
            _state.TurnManager.SetTurnOrder(new List<string> { "p0", "p1", "p2", "p3" });
            
            // Set up a basic task pool
            _state.CurrentTaskPool = TaskPool.AllTasks.Take(10).ToList();
        }

        [TestMethod]
        public void OnEnter_PlayerNotGuessed_StayInState()
        {
            var state = new GuessPhaseState();
            var result = state.OnEnter(_context);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
            Assert.AreEqual(GamePhase.GuessPhase, _state.Phase);
        }

        [TestMethod]
        public void OnEnter_PlayerAlreadyGuessed_SkipState()
        {
            _state.GamePlayers["p0"].HasSubmittedGuess = true;
            
            var state = new GuessPhaseState();
            var result = state.OnEnter(_context);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Value);
            Assert.IsInstanceOfType<EventCardPhaseState>(result.Value);
            Assert.AreEqual("p1", _state.TurnManager.CurrentPlayer);
        }

        [TestMethod]
        public void SubmitGuess_Valid_StoresGuessAndAdvances()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var poolIds = _state.CurrentTaskPool.Select(t => t.Id).Take(3).ToList();
            var guesses = new Dictionary<string, List<string>>
            {
                { "p1", [.. poolIds] },
                { "p2", [.. poolIds] },
                { "p3", [.. poolIds] }
            };

            var result = state.HandleCommand(_context, new SubmitGuessCommand("p0", guesses));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Value);
            Assert.IsTrue(_state.GamePlayers["p0"].HasSubmittedGuess);
            Assert.AreEqual(guesses, _state.GamePlayers["p0"].GuessSubmission);
        }

        [TestMethod]
        public void SubmitGuess_FirstGuess_TriggersCountdown()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var poolIds = _state.CurrentTaskPool.Select(t => t.Id).Take(3).ToList();
            var guesses = new Dictionary<string, List<string>>
            {
                { "p1", [.. poolIds] },
                { "p2", [.. poolIds] },
                { "p3", [.. poolIds] }
            };

            state.HandleCommand(_context, new SubmitGuessCommand("p0", guesses));

            Assert.IsTrue(_state.GuessCountdownActive);
            Assert.AreEqual("p0", _state.FirstGuessPlayerId);
            Assert.AreEqual(2, _state.GamePlayers["p1"].GuessCountdownTurnsRemaining);
            Assert.AreEqual(2, _state.GamePlayers["p2"].GuessCountdownTurnsRemaining);
            Assert.AreEqual(2, _state.GamePlayers["p3"].GuessCountdownTurnsRemaining);
            Assert.AreEqual(0, _state.GamePlayers["p0"].GuessCountdownTurnsRemaining);
        }

        [TestMethod]
        public void SubmitGuess_InvalidOpponent_ReturnsError()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var guesses = new Dictionary<string, List<string>>
            {
                { "invalid_id", ["T1", "T2", "T3"] }
            };

            var result = state.HandleCommand(_context, new SubmitGuessCommand("p0", guesses));

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNotNull(result.Error);
        }

        [TestMethod]
        public void SubmitGuess_WrongTaskCount_ReturnsError()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var guesses = new Dictionary<string, List<string>>
            {
                { "p1", ["T1", "T2"] }, // Only 2 tasks
                { "p2", ["T1", "T2", "T3"] },
                { "p3", ["T1", "T2", "T3"] }
            };

            var result = state.HandleCommand(_context, new SubmitGuessCommand("p0", guesses));

            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void SubmitGuess_DuplicateTaskIds_ReturnsError()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var guesses = new Dictionary<string, List<string>>
            {
                { "p1", ["T1", "T1", "T2"] }, // Duplicate T1
                { "p2", ["T1", "T2", "T3"] },
                { "p3", ["T1", "T2", "T3"] }
            };

            var result = state.HandleCommand(_context, new SubmitGuessCommand("p0", guesses));

            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void SkipGuess_Valid_AdvancesToNextPlayer()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var result = state.HandleCommand(_context, new SkipGuessCommand("p0"));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Value);
            Assert.IsInstanceOfType<EventCardPhaseState>(result.Value);
            Assert.IsFalse(_state.GamePlayers["p0"].HasSubmittedGuess);
        }

        [TestMethod]
        public void Tick_Timeout_AutoSkips()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var result = state.Tick(_context, DateTimeOffset.UtcNow.AddMilliseconds(_state.Config.GuessPhaseTimeoutMs + 100));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Value);
            Assert.IsInstanceOfType<EventCardPhaseState>(result.Value);
        }
    }
}
