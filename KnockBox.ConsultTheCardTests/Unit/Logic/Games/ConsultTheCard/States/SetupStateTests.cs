using KnockBox.Services.Logic.Games.ConsultTheCard.FSM;
using KnockBox.Services.Logic.Games.ConsultTheCard.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.ConsultTheCardTests.Unit.Logic.Games.ConsultTheCard.States
{
    [TestClass]
    public class SetupStateTests
    {
        private Mock<IRandomNumberService> _rng = default!;
        private Mock<ILogger> _logger = default!;
        private Mock<ILogger<ConsultTheCardGameState>> _stateLogger = default!;
        private ConsultTheCardGameState _state = default!;
        private ConsultTheCardGameContext _context = default!;

        [TestInitialize]
        public void Setup()
        {
            _rng = new Mock<IRandomNumberService>();
            int callCount = 0;
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int max, RandomType _) => { callCount++; return callCount % 2 == 0 ? 1 % max : 0; });
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int min, int max, RandomType _) => min);
            _logger = new Mock<ILogger>();
            _stateLogger = new Mock<ILogger<ConsultTheCardGameState>>();

            var host = new User("Host", "host-id");
            _state = new ConsultTheCardGameState(host, _stateLogger.Object);
            _context = new ConsultTheCardGameContext(_state, _rng.Object, _logger.Object);

            // Add 5 players.
            for (int i = 0; i < 5; i++)
            {
                _state.GamePlayers[$"p{i}"] = new ConsultTheCardPlayerState
                {
                    PlayerId = $"p{i}",
                    DisplayName = $"Player {i}"
                };
                _state.TurnOrder.Add($"p{i}");
            }
        }

        [TestMethod]
        public void OnEnter_IncrementsEliminationCycle()
        {
            Assert.AreEqual(0, _state.CurrentEliminationCycle);
            var setupState = new SetupState();
            setupState.OnEnter(_context);
            Assert.AreEqual(1, _state.CurrentEliminationCycle);
        }

        [TestMethod]
        public void OnEnter_AssignsRoles()
        {
            var setupState = new SetupState();
            setupState.OnEnter(_context);

            var players = _state.GamePlayers.Values.ToList();
            Assert.IsTrue(players.Any(p => p.Role == Role.Agent));
            Assert.IsTrue(players.Any(p => p.Role == Role.Insider));
        }

        [TestMethod]
        public void OnEnter_SetsPhaseToSetup()
        {
            var setupState = new SetupState();
            setupState.OnEnter(_context);
            Assert.AreEqual(ConsultTheCardGamePhase.Setup, _state.GamePhase);
        }

        [TestMethod]
        public void OnEnter_SetsWordPair()
        {
            var setupState = new SetupState();
            setupState.OnEnter(_context);
            Assert.IsNotNull(_state.CurrentWordPair);
            Assert.AreEqual(2, _state.CurrentWordPair.Length);
        }

        [TestMethod]
        public void Tick_ReturnsNullBeforeTimeout()
        {
            var setupState = new SetupState();
            setupState.OnEnter(_context);

            var result = setupState.Tick(_context, DateTimeOffset.UtcNow);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void Tick_TransitionsToCluePhaseAfterTimeout()
        {
            var setupState = new SetupState();
            setupState.OnEnter(_context);

            var result = setupState.Tick(_context, DateTimeOffset.UtcNow.AddSeconds(10));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<CluePhaseState>(result.Value);
        }

        [TestMethod]
        public void GetRemainingTime_ReturnsCountdown()
        {
            var setupState = new SetupState();
            setupState.OnEnter(_context);

            var remaining = setupState.GetRemainingTime(_context, DateTimeOffset.UtcNow);
            Assert.IsTrue(remaining.IsSuccess);
            Assert.IsTrue(remaining.Value.TotalMilliseconds > 0);
        }

        [TestMethod]
        public void HandleCommand_ReturnsNull()
        {
            var setupState = new SetupState();
            setupState.OnEnter(_context);

            var result = setupState.HandleCommand(_context, new SubmitClueCommand("p0", "test"));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }
    }
}
