using KnockBox.ConsultTheCard.Services.Logic.Games.FSM;
using KnockBox.ConsultTheCard.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.ConsultTheCard.Services.State.Games;
using KnockBox.ConsultTheCard.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.ConsultTheCard.Tests.Unit.Logic.Games.ConsultTheCard.States
{
    [TestClass]
    public class DiscussionPhaseStateTests
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
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _logger = new Mock<ILogger>();
            _stateLogger = new Mock<ILogger<ConsultTheCardGameState>>();

            var host = new User("Host", "host-id");
            _state = new ConsultTheCardGameState(host, _stateLogger.Object);
            _context = new ConsultTheCardGameContext(_state, _rng.Object, _logger.Object);

            AddPlayer("p0", "Player 0", Role.Agent);
            AddPlayer("p1", "Player 1", Role.Agent);
            AddPlayer("p2", "Player 2", Role.Insider);
            AddPlayer("p3", "Player 3", Role.Agent);
        }

        private void AddPlayer(string id, string name, Role role)
        {
            _state.GamePlayers[id] = new ConsultTheCardPlayerState
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
            Assert.AreEqual(ConsultTheCardGamePhase.Discussion, _state.Phase);
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

            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand("p0"));
            Assert.IsTrue(result.IsSuccess);
            Assert.Contains("p0", _state.EndGameVoteStatus.VotedToEnd);
        }

        [TestMethod]
        public void HandleCommand_VoteToEndGame_RescindsOnDoubleVote()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            discussion.HandleCommand(_context, new VoteToEndGameCommand("p0"));
            Assert.Contains("p0", _state.EndGameVoteStatus.VotedToEnd);
            Assert.IsTrue(_state.GamePlayers["p0"].HasVotedToEndGame);

            // Second vote rescinds.
            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand("p0"));
            Assert.IsTrue(result.IsSuccess);
            Assert.DoesNotContain("p0", _state.EndGameVoteStatus.VotedToEnd);
            Assert.IsFalse(_state.GamePlayers["p0"].HasVotedToEndGame);
        }

        [TestMethod]
        public void HandleCommand_VoteToEndGame_RejectsEliminatedPlayer()
        {
            _state.GamePlayers["p0"].IsEliminated = true;

            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand("p0"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_VoteToEndGame_MajorityTransitionsToGameOver()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            // Required votes = (4/2)+1 = 3.
            discussion.HandleCommand(_context, new VoteToEndGameCommand("p0"));
            discussion.HandleCommand(_context, new VoteToEndGameCommand("p1"));
            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand("p2"));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<GameOverState>(result.Value);
        }

        [TestMethod]
        public void HandleCommand_AdvanceToVote_HostOnly()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new AdvanceToVoteCommand("host-id"));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);
        }

        [TestMethod]
        public void HandleCommand_AdvanceToVote_RejectsNonHost()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new AdvanceToVoteCommand("p0"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void Tick_WithTimersEnabled_AutoAdvancesOnTimeout()
        {
            _state.Config.EnableTimers = true;
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(10));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);
        }

        [TestMethod]
        public void Tick_WithTimersDisabled_DoesNotAutoAdvance()
        {
            _state.Config.EnableTimers = false;
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(10));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void Tick_BeforeTimeout_ReturnsNull()
        {
            _state.Config.EnableTimers = true;
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

            var result = discussion.HandleCommand(_context, new SkipRemainingTimeCommand("p0"));
            Assert.IsTrue(result.IsSuccess);
            Assert.Contains("p0", _state.SkipTimeVoteStatus.VotedToEnd);
            Assert.IsTrue(_state.GamePlayers["p0"].HasVotedToSkipTime);
        }

        [TestMethod]
        public void HandleCommand_SkipRemainingTime_RescindsOnDoubleVote()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            discussion.HandleCommand(_context, new SkipRemainingTimeCommand("p0"));
            Assert.Contains("p0", _state.SkipTimeVoteStatus.VotedToEnd);

            // Second vote rescinds.
            var result = discussion.HandleCommand(_context, new SkipRemainingTimeCommand("p0"));
            Assert.IsTrue(result.IsSuccess);
            Assert.DoesNotContain("p0", _state.SkipTimeVoteStatus.VotedToEnd);
            Assert.IsFalse(_state.GamePlayers["p0"].HasVotedToSkipTime);
        }

        [TestMethod]
        public void HandleCommand_SkipRemainingTime_MajorityTransitionsToReveal()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            // Required votes = (4/2)+1 = 3.
            discussion.HandleCommand(_context, new SkipRemainingTimeCommand("p0"));
            discussion.HandleCommand(_context, new SkipRemainingTimeCommand("p1"));
            var result = discussion.HandleCommand(_context, new SkipRemainingTimeCommand("p2"));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);
        }

        [TestMethod]
        public void HandleCommand_SkipRemainingTime_RejectsEliminatedPlayer()
        {
            _state.GamePlayers["p0"].IsEliminated = true;

            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new SkipRemainingTimeCommand("p0"));
            Assert.IsFalse(result.IsSuccess);
        }

        // ── CastVote tests (inline voting in discussion phase) ────────────────

        [TestMethod]
        public void HandleCommand_CastVote_SelectsTarget()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new CastVoteCommand("p0", "p1"));
            Assert.IsTrue(result.IsSuccess);

            var voter = _context.GetPlayer("p0")!;
            Assert.AreEqual("p1", voter.VoteTargetId);
            Assert.IsFalse(voter.HasVoted, "CastVote should not lock in the vote.");
        }

        [TestMethod]
        public void HandleCommand_CastVote_RejectsSelfVote()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new CastVoteCommand("p0", "p0"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_CastVote_RejectsVoteForEliminated()
        {
            _state.GamePlayers["p1"].IsEliminated = true;

            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new CastVoteCommand("p0", "p1"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_CastVote_RejectsIfAlreadyLockedIn()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            _state.GamePlayers["p0"].VoteTargetId = "p1";
            _state.GamePlayers["p0"].HasVoted = true;

            var result = discussion.HandleCommand(_context, new CastVoteCommand("p0", "p2"));
            Assert.IsFalse(result.IsSuccess);
        }

        // ── LockInVote tests ──────────────────────────────────────────────────

        [TestMethod]
        public void HandleCommand_LockInVote_LocksIn()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            // First select a target.
            discussion.HandleCommand(_context, new CastVoteCommand("p0", "p1"));

            var result = discussion.HandleCommand(_context, new LockInVoteCommand("p0"));
            Assert.IsTrue(result.IsSuccess);

            var voter = _context.GetPlayer("p0")!;
            Assert.IsTrue(voter.HasVoted);
            Assert.HasCount(1, _state.CurrentRoundVotes);
        }

        [TestMethod]
        public void HandleCommand_LockInVote_RejectsWithoutTarget()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new LockInVoteCommand("p0"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_LockInVote_AllVoted_TransitionsToReveal()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            // All 4 players select targets and lock in.
            discussion.HandleCommand(_context, new CastVoteCommand("p0", "p1"));
            discussion.HandleCommand(_context, new LockInVoteCommand("p0"));

            discussion.HandleCommand(_context, new CastVoteCommand("p1", "p2"));
            discussion.HandleCommand(_context, new LockInVoteCommand("p1"));

            discussion.HandleCommand(_context, new CastVoteCommand("p2", "p3"));
            discussion.HandleCommand(_context, new LockInVoteCommand("p2"));

            discussion.HandleCommand(_context, new CastVoteCommand("p3", "p1"));
            var result = discussion.HandleCommand(_context, new LockInVoteCommand("p3"));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<RevealPhaseState>(result.Value);

            // p1 should be eliminated (2 votes vs 1 each for others).
            Assert.IsTrue(_state.GamePlayers["p1"].IsEliminated);
        }

        // ── Vote to end game re-vote after rescind ────────────────────────────

        [TestMethod]
        public void HandleCommand_VoteToEndGame_CanReVoteAfterRescind()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            // Vote, rescind, vote again.
            discussion.HandleCommand(_context, new VoteToEndGameCommand("p0"));
            discussion.HandleCommand(_context, new VoteToEndGameCommand("p0")); // rescind
            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand("p0")); // re-vote

            Assert.IsTrue(result.IsSuccess);
            Assert.Contains("p0", _state.EndGameVoteStatus.VotedToEnd);
            Assert.IsTrue(_state.GamePlayers["p0"].HasVotedToEndGame);
        }
    }
}
