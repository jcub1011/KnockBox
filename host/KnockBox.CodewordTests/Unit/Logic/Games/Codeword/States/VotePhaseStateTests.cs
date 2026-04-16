using KnockBox.Codeword.Services.Logic.Games.FSM;
using KnockBox.Codeword.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.Codeword.Tests.Unit.Logic.Games.Codeword.States
{
    [TestClass]
    public class VotePhaseStateTests
    {
        private Mock<IRandomNumberService> _rng = default!;
        private Mock<ILogger> _logger = default!;
        private Mock<ILogger<CodewordGameState>> _stateLogger = default!;
        private CodewordGameState _state = default!;
        private CodewordGameContext _context = default!;

        [TestInitialize]
        public void Setup()
        {
            _rng = new Mock<IRandomNumberService>();
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _logger = new Mock<ILogger>();
            _stateLogger = new Mock<ILogger<CodewordGameState>>();

            var host = new User("Host", "host-id");
            _state = new CodewordGameState(host, _stateLogger.Object);
            _context = new CodewordGameContext(_state, _rng.Object, _logger.Object);

            AddPlayer("p0", "Player 0", Role.Agent);
            AddPlayer("p1", "Player 1", Role.Insider);
            AddPlayer("p2", "Player 2", Role.Agent);
        }

        private void AddPlayer(string id, string name, Role role)
        {
            _state.GamePlayers[id] = new CodewordPlayerState
            {
                PlayerId = id,
                DisplayName = name,
                Role = role,
                SecretWord = role == Role.Agent ? "Ocean" : "Lake"
            };
            _state.TurnManager.TurnOrder.Add(id);
        }

        [TestMethod]
        public void OnEnter_SetsPhaseToVoting()
        {
            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);
            Assert.AreEqual(CodewordGamePhase.Voting, _state.Phase);
        }

        [TestMethod]
        public void HandleCommand_ValidVote_RecordsVote()
        {
            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            var result = voteState.HandleCommand(_context, new CastVoteCommand("p0", "p1"));
            Assert.IsTrue(result.IsSuccess);

            var voter = _context.GetPlayer("p0")!;
            Assert.IsTrue(voter.HasVoted);
            Assert.AreEqual("p1", voter.VoteTargetId);
        }

        [TestMethod]
        public void HandleCommand_RejectsSelfVote()
        {
            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            var result = voteState.HandleCommand(_context, new CastVoteCommand("p0", "p0"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_RejectsVoteForEliminated()
        {
            _state.GamePlayers["p1"].IsEliminated = true;

            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            var result = voteState.HandleCommand(_context, new CastVoteCommand("p0", "p1"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_RejectsDoubleVote()
        {
            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            voteState.HandleCommand(_context, new CastVoteCommand("p0", "p1"));
            var result = voteState.HandleCommand(_context, new CastVoteCommand("p0", "p2"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_RejectsEliminatedVoter()
        {
            _state.GamePlayers["p0"].IsEliminated = true;

            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            var result = voteState.HandleCommand(_context, new CastVoteCommand("p0", "p1"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_AllVoted_TalliesAndTransitions()
        {
            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            voteState.HandleCommand(_context, new CastVoteCommand("p0", "p1"));
            voteState.HandleCommand(_context, new CastVoteCommand("p1", "p2"));
            var result = voteState.HandleCommand(_context, new CastVoteCommand("p2", "p1"));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);

            // p1 should be eliminated (2 votes vs 1).
            Assert.IsTrue(_state.GamePlayers["p1"].IsEliminated);
            Assert.IsNotNull(_state.LastElimination);
            Assert.AreEqual("p1", _state.LastElimination.PlayerId);
            Assert.IsFalse(_state.LastElimination.WasTie);
        }

        [TestMethod]
        public void HandleCommand_TiedVote_SetsWasTie()
        {
            AddPlayer("p3", "Player 3", Role.Agent);

            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            voteState.HandleCommand(_context, new CastVoteCommand("p0", "p1"));
            voteState.HandleCommand(_context, new CastVoteCommand("p1", "p0"));
            voteState.HandleCommand(_context, new CastVoteCommand("p2", "p3"));
            var result = voteState.HandleCommand(_context, new CastVoteCommand("p3", "p2"));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);
            Assert.IsNotNull(_state.LastElimination);
            Assert.IsTrue(_state.LastElimination.WasTie);

            // No one should be eliminated in a tie.
            Assert.IsTrue(_state.GamePlayers.Values.All(p => !p.IsEliminated));
        }

        [TestMethod]
        public void Tick_WithTimersEnabled_AbstainsAndTransitions()
        {
            _state.Config.EnableTimers = true;
            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            var result = voteState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);

            // All should be marked as voted (abstained).
            Assert.IsTrue(_context.GetAlivePlayers().All(p => p.HasVoted));
        }

        [TestMethod]
        public void Tick_WithTimersDisabled_DoesNotTransition()
        {
            _state.Config.EnableTimers = false;
            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            var result = voteState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }
    }
}
