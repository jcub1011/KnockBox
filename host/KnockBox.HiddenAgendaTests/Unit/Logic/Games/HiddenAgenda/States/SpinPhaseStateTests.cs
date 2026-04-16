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
    public class SpinPhaseStateTests
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
            _rng.Setup(r => r.GetRandomInt(3, 13, It.IsAny<RandomType>())).Returns(7);
            
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
                    DisplayName = $"Player {i}"
                };
            }
            _state.TurnManager.SetTurnOrder(new List<string> { "p0", "p1", "p2", "p3" });
        }

        [TestMethod]
        public void SpinCommand_Valid_StoresResultAndTransitionsToMove()
        {
            var state = new SpinPhaseState();
            state.OnEnter(_context);

            var result = state.HandleCommand(_context, new SpinCommand("p0"));
            
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<MovePhaseState>(result.Value);
            Assert.AreEqual(7, _state.GamePlayers["p0"].LastSpinResult);
            Assert.AreEqual(7, _state.CurrentSpinResult);
        }

        [TestMethod]
        public void SpinCommand_DetourPending_TeleportsAndTransitionsToDraw()
        {
            var p0 = _state.GamePlayers["p0"];
            var p1 = _state.GamePlayers["p1"];
            p0.DetourPending = true;
            p0.DetourTargetPlayerId = "p1";
            p1.LastSpinResult = 12;
            p1.LastMoveDestination = 5;

            var state = new SpinPhaseState();
            state.OnEnter(_context);

            var result = state.HandleCommand(_context, new SpinCommand("p0"));
            
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<DrawPhaseState>(result.Value);
            Assert.AreEqual(5, p0.CurrentSpaceId);
            Assert.AreEqual(12, p0.LastSpinResult);
            Assert.IsFalse(p0.DetourPending);
        }

        [TestMethod]
        public void Tick_AutoSpinsAfterTimeout()
        {
            _state.Config.EnableTimers = true;
            var state = new SpinPhaseState();
            state.OnEnter(_context);

            var result = state.Tick(_context, DateTimeOffset.UtcNow.AddMilliseconds(_state.Config.SpinPhaseTimeoutMs + 100));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<MovePhaseState>(result.Value);
            Assert.AreEqual(7, _state.GamePlayers["p0"].LastSpinResult);
        }

        [TestMethod]
        public void Tick_TimersDisabled_DoesNotAutoAdvance()
        {
            var state = new SpinPhaseState();
            state.OnEnter(_context);

            var result = state.Tick(_context, DateTimeOffset.UtcNow.AddMilliseconds(_state.Config.SpinPhaseTimeoutMs + 100));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void CallVote_AnyPlayer_TransitionsToFinalGuess()
        {
            var state = new SpinPhaseState();
            state.OnEnter(_context);

            var result = state.HandleCommand(_context, new CallVoteCommand("p1"));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<FinalGuessState>(result.Value);
        }
    }
}