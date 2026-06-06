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

            var host = UserFactory.Create("Host", Guid.Parse("00000000-0000-0000-0000-000000000001"));
            _state = new HiddenAgendaGameState(host, _stateLogger.Object);
            _state.BoardGraph = BoardDefinitions.CreateGrandCircuit();
            _context = new HiddenAgendaGameContext(_state, _rng.Object, _logger.Object);

            var playerIds = new[]
            {
                Guid.Parse("10000000-0000-0000-0000-000000000000"),
                Guid.Parse("20000000-0000-0000-0000-000000000000"),
                Guid.Parse("30000000-0000-0000-0000-000000000000"),
                Guid.Parse("40000000-0000-0000-0000-000000000000"),
            };
            for (int i = 0; i < 4; i++)
            {
                var pid = playerIds[i];
                _state.GamePlayers[pid] = new HiddenAgendaPlayerState
                {
                    PlayerId = pid,
                    DisplayName = $"Player {i}",
                    CurrentSpaceId = 0
                };
            }
            _state.TurnManager.SetTurnOrder(playerIds);
            
            // Set up a basic task pool
            _state.CurrentTaskPool = TaskPool.AllTasks.Take(10).ToList();
        }

        [TestMethod]
        public void OnEnter_PlayerNotGuessed_StayInState()
        {
            var state = new GuessPhaseState();
            var result = state.OnEnter(_context);

            Assert.IsNull(result.Value);
            Assert.AreEqual(GamePhase.GuessPhase, _state.Phase);
        }

        [TestMethod]
        public void OnEnter_PlayerAlreadyGuessed_SkipState()
        {
            _state.GamePlayers[Guid.Parse("10000000-0000-0000-0000-000000000000")].HasSubmittedGuess = true;
            
            var state = new GuessPhaseState();
            var result = state.OnEnter(_context);

            Assert.IsNotNull(result.Value);
            Assert.IsInstanceOfType<EventCardPhaseState>(result.Value);
            Assert.AreEqual(Guid.Parse("20000000-0000-0000-0000-000000000000"), (Guid?)_state.TurnManager.CurrentPlayer);
        }

        [TestMethod]
        public void SubmitGuess_Valid_StoresGuessAndAdvances()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var poolIds = _state.CurrentTaskPool.Select(t => t.Id).Take(3).ToList();
            var guesses = new Dictionary<Guid, List<string>>
            {
                { Guid.Parse("20000000-0000-0000-0000-000000000000"), [.. poolIds] },
                { Guid.Parse("30000000-0000-0000-0000-000000000000"), [.. poolIds] },
                { Guid.Parse("40000000-0000-0000-0000-000000000000"), [.. poolIds] }
            };

            var result = state.HandleCommand(_context, new SubmitGuessCommand(Guid.Parse("10000000-0000-0000-0000-000000000000"), guesses));

            Assert.IsNotNull(result.Value);
            Assert.IsTrue(_state.GamePlayers[Guid.Parse("10000000-0000-0000-0000-000000000000")].HasSubmittedGuess);
            Assert.AreEqual(guesses, _state.GamePlayers[Guid.Parse("10000000-0000-0000-0000-000000000000")].GuessSubmission);
        }

        [TestMethod]
        public void SubmitGuess_InvalidOpponent_ReturnsError()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var guesses = new Dictionary<Guid, List<string>>
            {
                { Guid.NewGuid(), ["T1", "T2", "T3"] }
            };

            var result = state.HandleCommand(_context, new SubmitGuessCommand(Guid.Parse("10000000-0000-0000-0000-000000000000"), guesses));

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNotNull(result.Error);
        }

        [TestMethod]
        public void SubmitGuess_WrongTaskCount_ReturnsError()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var guesses = new Dictionary<Guid, List<string>>
            {
                { Guid.Parse("20000000-0000-0000-0000-000000000000"), ["T1", "T2"] }, // Only 2 tasks
                { Guid.Parse("30000000-0000-0000-0000-000000000000"), ["T1", "T2", "T3"] },
                { Guid.Parse("40000000-0000-0000-0000-000000000000"), ["T1", "T2", "T3"] }
            };

            var result = state.HandleCommand(_context, new SubmitGuessCommand(Guid.Parse("10000000-0000-0000-0000-000000000000"), guesses));

            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void SubmitGuess_DuplicateTaskIds_ReturnsError()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var guesses = new Dictionary<Guid, List<string>>
            {
                { Guid.Parse("20000000-0000-0000-0000-000000000000"), ["T1", "T1", "T2"] }, // Duplicate T1
                { Guid.Parse("30000000-0000-0000-0000-000000000000"), ["T1", "T2", "T3"] },
                { Guid.Parse("40000000-0000-0000-0000-000000000000"), ["T1", "T2", "T3"] }
            };

            var result = state.HandleCommand(_context, new SubmitGuessCommand(Guid.Parse("10000000-0000-0000-0000-000000000000"), guesses));

            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void SkipGuess_Valid_AdvancesToNextPlayer()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var result = state.HandleCommand(_context, new SkipGuessCommand(Guid.Parse("10000000-0000-0000-0000-000000000000")));

            Assert.IsNotNull(result.Value);
            Assert.IsInstanceOfType<EventCardPhaseState>(result.Value);
            Assert.IsFalse(_state.GamePlayers[Guid.Parse("10000000-0000-0000-0000-000000000000")].HasSubmittedGuess);
        }

        [TestMethod]
        public void Tick_Timeout_AutoSkips()
        {
            var state = new GuessPhaseState();
            state.OnEnter(_context);

            var result = state.Tick(_context, DateTimeOffset.UtcNow.AddMilliseconds(_state.Settings.GuessPhaseTimeoutMs + 100));

            Assert.IsNotNull(result.Value);
            Assert.IsInstanceOfType<EventCardPhaseState>(result.Value);
        }
    }
}
