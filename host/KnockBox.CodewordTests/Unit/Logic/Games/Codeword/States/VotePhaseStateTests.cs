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

        private Guid _p0Id = default!;
        private Guid _p1Id = default!;
        private Guid _p2Id = default!;
        private Guid _p3Id = default!;

        [TestInitialize]
        public void Setup()
        {
            _p0Id = Guid.NewGuid();
            _p1Id = Guid.NewGuid();
            _p2Id = Guid.NewGuid();
            _p3Id = Guid.NewGuid();

            _rng = new Mock<IRandomNumberService>();
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _logger = new Mock<ILogger>();
            _stateLogger = new Mock<ILogger<CodewordGameState>>();

            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new CodewordGameState(host, _stateLogger.Object);
            _context = new CodewordGameContext(_state, _rng.Object, _logger.Object);

            AddPlayer(_p0Id, "Player 0", Role.Agent);
            AddPlayer(_p1Id, "Player 1", Role.Insider);
            AddPlayer(_p2Id, "Player 2", Role.Agent);
        }

        private void AddPlayer(Guid id, string name, Role role)
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

            var result = voteState.HandleCommand(_context, new CastVoteCommand(_p0Id, _p1Id));
            Assert.IsTrue(result.IsSuccess);

            var voter = _context.GetPlayer(_p0Id)!;
            Assert.IsTrue(voter.HasVoted);
            Assert.AreEqual(_p1Id, voter.VoteTargetId);
        }

        [TestMethod]
        public void HandleCommand_RejectsSelfVote()
        {
            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            var result = voteState.HandleCommand(_context, new CastVoteCommand(_p0Id, _p0Id));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_RejectsVoteForEliminated()
        {
            _state.GamePlayers[_p1Id].IsEliminated = true;

            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            var result = voteState.HandleCommand(_context, new CastVoteCommand(_p0Id, _p1Id));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_RejectsDoubleVote()
        {
            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            voteState.HandleCommand(_context, new CastVoteCommand(_p0Id, _p1Id));
            var result = voteState.HandleCommand(_context, new CastVoteCommand(_p0Id, _p2Id));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_RejectsEliminatedVoter()
        {
            _state.GamePlayers[_p0Id].IsEliminated = true;

            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            var result = voteState.HandleCommand(_context, new CastVoteCommand(_p0Id, _p1Id));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_AllVoted_TalliesAndTransitions()
        {
            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            voteState.HandleCommand(_context, new CastVoteCommand(_p0Id, _p1Id));
            voteState.HandleCommand(_context, new CastVoteCommand(_p1Id, _p2Id));
            var result = voteState.HandleCommand(_context, new CastVoteCommand(_p2Id, _p1Id));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);

            // p1 should be eliminated (2 votes vs 1).
            Assert.IsTrue(_state.GamePlayers[_p1Id].IsEliminated);
            Assert.IsNotNull(_state.LastElimination);
            Assert.AreEqual(_p1Id, _state.LastElimination.PlayerId);
            Assert.IsFalse(_state.LastElimination.WasTie);
        }

        [TestMethod]
        public void HandleCommand_TiedVote_SetsWasTie()
        {
            AddPlayer(_p3Id, "Player 3", Role.Agent);

            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            voteState.HandleCommand(_context, new CastVoteCommand(_p0Id, _p1Id));
            voteState.HandleCommand(_context, new CastVoteCommand(_p1Id, _p0Id));
            voteState.HandleCommand(_context, new CastVoteCommand(_p2Id, _p3Id));
            var result = voteState.HandleCommand(_context, new CastVoteCommand(_p3Id, _p2Id));

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
            _state.UpdateSettings(s => s with { EnableTimers = true });
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
            _state.UpdateSettings(s => s with { EnableTimers = false });
            var voteState = new VotePhaseState();
            voteState.OnEnter(_context);

            var result = voteState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }
    }
}
