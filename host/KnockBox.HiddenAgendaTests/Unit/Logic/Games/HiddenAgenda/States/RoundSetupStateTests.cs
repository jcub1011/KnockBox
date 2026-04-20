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
    public class RoundSetupStateTests
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
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int max, RandomType _) => 0);
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int min, int max, RandomType _) => min);
            
            _logger = new Mock<ILogger>();
            _stateLogger = new Mock<ILogger<HiddenAgendaGameState>>();

            var host = new User("Host", "host-id");
            _state = new HiddenAgendaGameState(host, _stateLogger.Object);
            _state.BoardGraph = BoardDefinitions.CreateGrandCircuit();
            _context = new HiddenAgendaGameContext(_state, _rng.Object, _logger.Object);

            // Add 4 players
            for (int i = 0; i < 4; i++)
            {
                var pid = $"p{i}";
                _state.GamePlayers[pid] = new HiddenAgendaPlayerState
                {
                    PlayerId = pid,
                    DisplayName = $"Player {i}"
                };
            }
        }

        [TestMethod]
        public void OnEnter_IncrementsCurrentRound()
        {
            Assert.AreEqual(0, _state.CurrentRound);
            var state = new RoundSetupState();
            state.OnEnter(_context);
            Assert.AreEqual(1, _state.CurrentRound);
        }

        [TestMethod]
        public void OnEnter_GeneratesTaskPool()
        {
            var state = new RoundSetupState();
            state.OnEnter(_context);
            Assert.IsNotNull(_state.CurrentTaskPool);
            Assert.IsTrue(_state.CurrentTaskPool.Count > 0);
        }

        [TestMethod]
        public void OnEnter_DrawsTasksForPlayers()
        {
            var state = new RoundSetupState();
            state.OnEnter(_context);
            foreach (var player in _state.GamePlayers.Values)
            {
                Assert.AreEqual(3, player.SecretTasks.Count);
            }
        }

        [TestMethod]
        public void OnEnter_RandomizesTurnOrderOnFirstRound()
        {
            _state.CurrentRound = 0;
            var state = new RoundSetupState();
            state.OnEnter(_context);
            Assert.AreEqual(4, _state.TurnManager.TurnOrder.Count);
        }

        [TestMethod]
        public void OnEnter_SetsStartingPositions()
        {
            foreach (var player in _state.GamePlayers.Values)
            {
                player.CurrentSpaceId = 10; // Move them away first
            }

            var state = new RoundSetupState();
            state.OnEnter(_context);

            foreach (var player in _state.GamePlayers.Values)
            {
                Assert.AreEqual(0, player.CurrentSpaceId);
            }
        }

        [TestMethod]
        public void Tick_TransitionsAfterTimeout()
        {
            var state = new RoundSetupState();
            state.OnEnter(_context);

            var result = state.Tick(_context, DateTimeOffset.UtcNow.AddMilliseconds(_state.Config.RoundSetupTimeoutMs + 100));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<EventCardPhaseState>(result.Value);
        }

        [TestMethod]
        public void HandleCommand_ReturnsNull()
        {
            var state = new RoundSetupState();
            state.OnEnter(_context);
            var result = state.HandleCommand(_context, new SpinCommand("p0"));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }
    }
}