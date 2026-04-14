using System;
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
    public class MovePhaseStateTests
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
                    CurrentSpaceId = 0,
                    LastSpinResult = 3
                };
            }
            _state.TurnManager.SetTurnOrder(new List<string> { "p0", "p1", "p2", "p3" });
        }

        [TestMethod]
        public void OnEnter_CalculatesReachableSpaces()
        {
            var state = new MovePhaseState();
            state.OnEnter(_context);

            Assert.IsNotNull(_state.ReachableSpaces);
            // From space 0 with spin 3, can reach space 3 (0-1-2-3) or others if shortcuts?
            // Space 2 has shortcut to 20, so 0-1-2-20 is also 3 steps.
            Assert.IsTrue(_state.ReachableSpaces.Any(s => s.Id == 3));
            Assert.IsTrue(_state.ReachableSpaces.Any(s => s.Id == 20));
        }

        [TestMethod]
        public void SelectDestination_Valid_UpdatesPosition()
        {
            var state = new MovePhaseState();
            state.OnEnter(_context);

            var result = state.HandleCommand(_context, new SelectDestinationCommand("p0", 3));
            
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<DrawPhaseState>(result.Value);
            Assert.AreEqual(3, _state.GamePlayers["p0"].CurrentSpaceId);
            Assert.AreEqual(1, _state.GamePlayers["p0"].MovementHistory.Count);
        }

        [TestMethod]
        public void SelectDestination_Invalid_ReturnsError()
        {
            var state = new MovePhaseState();
            state.OnEnter(_context);

            var result = state.HandleCommand(_context, new SelectDestinationCommand("p0", 10));
            
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void Tick_AutoSelectsDestinationAfterTimeout()
        {
            var state = new MovePhaseState();
            state.OnEnter(_context);

            var result = state.Tick(_context, DateTimeOffset.UtcNow.AddMilliseconds(_state.Config.MovePhaseTimeoutMs + 100));
            
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<DrawPhaseState>(result.Value);
            Assert.IsNotNull(_state.GamePlayers["p0"].LastMoveDestination);
        }
    }
}