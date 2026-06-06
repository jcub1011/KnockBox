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
    public class DiscussionPhaseStateTests
    {
        private Mock<IRandomNumberService> _rng = default!;
        private Mock<ILogger> _logger = default!;
        private Mock<ILogger<CodewordGameState>> _stateLogger = default!;
        private CodewordGameState _state = default!;
        private CodewordGameContext _context = default!;

        private Guid _hostId = default!;
        private Guid _p0Id = default!;
        private Guid _p1Id = default!;
        private Guid _p2Id = default!;
        private Guid _p3Id = default!;

        [TestInitialize]
        public void Setup()
        {
            _hostId = Guid.NewGuid();
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

            var host = UserFactory.Create("Host", _hostId);
            _state = new CodewordGameState(host, _stateLogger.Object);
            _context = new CodewordGameContext(_state, _rng.Object, _logger.Object);

            AddPlayer(_p0Id, "Player 0", Role.Agent);
            AddPlayer(_p1Id, "Player 1", Role.Agent);
            AddPlayer(_p2Id, "Player 2", Role.Insider);
            AddPlayer(_p3Id, "Player 3", Role.Agent);
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
        public void OnEnter_SetsPhaseToDiscussion()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);
            Assert.AreEqual(CodewordGamePhase.Discussion, _state.Phase);
        }

        [TestMethod]
        public void OnEnter_SetsEndGameVoteStatus()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            Assert.IsEmpty(_state.EndGameVoteStatus.VotedToEnd);
            Assert.IsGreaterThan(0, _state.EndGameVoteStatus.RequiredVotes);
        }

        [TestMethod]
        public void HandleCommand_VoteToEndGame_TracksVote()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand(_p0Id));
            Assert.IsTrue(result.IsSuccess);
            Assert.Contains(_p0Id, _state.EndGameVoteStatus.VotedToEnd);
        }

        [TestMethod]
        public void HandleCommand_VoteToEndGame_RescindsOnDoubleVote()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            discussion.HandleCommand(_context, new VoteToEndGameCommand(_p0Id));
            Assert.Contains(_p0Id, _state.EndGameVoteStatus.VotedToEnd);
            Assert.IsTrue(_state.GamePlayers[_p0Id].HasVotedToEndGame);

            // Second vote rescinds.
            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand(_p0Id));
            Assert.IsTrue(result.IsSuccess);
            Assert.DoesNotContain(_p0Id, _state.EndGameVoteStatus.VotedToEnd);
            Assert.IsFalse(_state.GamePlayers[_p0Id].HasVotedToEndGame);
        }

        [TestMethod]
        public void HandleCommand_VoteToEndGame_RejectsEliminatedPlayer()
        {
            _state.GamePlayers[_p0Id].IsEliminated = true;

            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand(_p0Id));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_VoteToEndGame_MajorityTransitionsToGameOver()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            // Required votes = (4/2)+1 = 3.
            discussion.HandleCommand(_context, new VoteToEndGameCommand(_p0Id));
            discussion.HandleCommand(_context, new VoteToEndGameCommand(_p1Id));
            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand(_p2Id));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<GameOverState>(result.Value);
        }

        [TestMethod]
        public void HandleCommand_AdvanceToVote_HostOnly()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new AdvanceToVoteCommand(_hostId));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);
        }

        [TestMethod]
        public void HandleCommand_AdvanceToVote_RejectsNonHost()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new AdvanceToVoteCommand(_p0Id));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void Tick_WithTimersEnabled_AutoAdvancesOnTimeout()
        {
            _state.UpdateSettings(s => s with { EnableTimers = true });
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(10));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);
        }

        [TestMethod]
        public void Tick_WithTimersDisabled_DoesNotAutoAdvance()
        {
            _state.UpdateSettings(s => s with { EnableTimers = false });
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(10));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void Tick_BeforeTimeout_ReturnsNull()
        {
            _state.UpdateSettings(s => s with { EnableTimers = true });
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.Tick(_context, DateTimeOffset.UtcNow);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }

        // ── Skip time rescinding tests ────────────────────────────────────────

        [TestMethod]
        public void HandleCommand_SkipRemainingTime_TracksVote()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new SkipRemainingTimeCommand(_p0Id));
            Assert.IsTrue(result.IsSuccess);
            Assert.Contains(_p0Id, _state.SkipTimeVoteStatus.VotedToEnd);
            Assert.IsTrue(_state.GamePlayers[_p0Id].HasVotedToSkipTime);
        }

        [TestMethod]
        public void HandleCommand_SkipRemainingTime_RescindsOnDoubleVote()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            discussion.HandleCommand(_context, new SkipRemainingTimeCommand(_p0Id));
            Assert.Contains(_p0Id, _state.SkipTimeVoteStatus.VotedToEnd);

            // Second vote rescinds.
            var result = discussion.HandleCommand(_context, new SkipRemainingTimeCommand(_p0Id));
            Assert.IsTrue(result.IsSuccess);
            Assert.DoesNotContain(_p0Id, _state.SkipTimeVoteStatus.VotedToEnd);
            Assert.IsFalse(_state.GamePlayers[_p0Id].HasVotedToSkipTime);
        }

        [TestMethod]
        public void HandleCommand_SkipRemainingTime_MajorityTransitionsToReveal()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            // Required votes = (4/2)+1 = 3.
            discussion.HandleCommand(_context, new SkipRemainingTimeCommand(_p0Id));
            discussion.HandleCommand(_context, new SkipRemainingTimeCommand(_p1Id));
            var result = discussion.HandleCommand(_context, new SkipRemainingTimeCommand(_p2Id));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);
        }

        [TestMethod]
        public void HandleCommand_SkipRemainingTime_RejectsEliminatedPlayer()
        {
            _state.GamePlayers[_p0Id].IsEliminated = true;

            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new SkipRemainingTimeCommand(_p0Id));
            Assert.IsFalse(result.IsSuccess);
        }

        // ── CastVote tests (inline voting in discussion phase) ────────────────

        [TestMethod]
        public void HandleCommand_CastVote_SelectsTarget()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new CastVoteCommand(_p0Id, _p1Id));
            Assert.IsTrue(result.IsSuccess);

            var voter = _context.GetPlayer(_p0Id)!;
            Assert.AreEqual(_p1Id, voter.VoteTargetId);
            Assert.IsFalse(voter.HasVoted, "CastVote should not lock in the vote.");
        }

        [TestMethod]
        public void HandleCommand_CastVote_RejectsSelfVote()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new CastVoteCommand(_p0Id, _p0Id));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_CastVote_RejectsVoteForEliminated()
        {
            _state.GamePlayers[_p1Id].IsEliminated = true;

            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new CastVoteCommand(_p0Id, _p1Id));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_CastVote_RejectsIfAlreadyLockedIn()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            _state.GamePlayers[_p0Id].VoteTargetId = _p1Id;
            _state.GamePlayers[_p0Id].HasVoted = true;

            var result = discussion.HandleCommand(_context, new CastVoteCommand(_p0Id, _p2Id));
            Assert.IsFalse(result.IsSuccess);
        }

        // ── LockInVote tests ──────────────────────────────────────────────────

        [TestMethod]
        public void HandleCommand_LockInVote_LocksIn()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            // First select a target.
            discussion.HandleCommand(_context, new CastVoteCommand(_p0Id, _p1Id));

            var result = discussion.HandleCommand(_context, new LockInVoteCommand(_p0Id));
            Assert.IsTrue(result.IsSuccess);

            var voter = _context.GetPlayer(_p0Id)!;
            Assert.IsTrue(voter.HasVoted);
            Assert.HasCount(1, _state.CurrentRoundVotes);
        }

        [TestMethod]
        public void HandleCommand_LockInVote_RejectsWithoutTarget()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new LockInVoteCommand(_p0Id));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_LockInVote_AllVoted_TransitionsToReveal()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            // All 4 players select targets and lock in.
            discussion.HandleCommand(_context, new CastVoteCommand(_p0Id, _p1Id));
            discussion.HandleCommand(_context, new LockInVoteCommand(_p0Id));

            discussion.HandleCommand(_context, new CastVoteCommand(_p1Id, _p2Id));
            discussion.HandleCommand(_context, new LockInVoteCommand(_p1Id));

            discussion.HandleCommand(_context, new CastVoteCommand(_p2Id, _p3Id));
            discussion.HandleCommand(_context, new LockInVoteCommand(_p2Id));

            discussion.HandleCommand(_context, new CastVoteCommand(_p3Id, _p1Id));
            var result = discussion.HandleCommand(_context, new LockInVoteCommand(_p3Id));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);

            // p1 should be eliminated (2 votes vs 1 each for others).
            Assert.IsTrue(_state.GamePlayers[_p1Id].IsEliminated);
        }

        // ── Vote to end game re-vote after rescind ────────────────────────────

        [TestMethod]
        public void HandleCommand_VoteToEndGame_CanReVoteAfterRescind()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            // Vote, rescind, vote again.
            discussion.HandleCommand(_context, new VoteToEndGameCommand(_p0Id));
            discussion.HandleCommand(_context, new VoteToEndGameCommand(_p0Id)); // rescind
            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand(_p0Id)); // re-vote

            Assert.IsTrue(result.IsSuccess);
            Assert.Contains(_p0Id, _state.EndGameVoteStatus.VotedToEnd);
            Assert.IsTrue(_state.GamePlayers[_p0Id].HasVotedToEndGame);
        }
    }
}
