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

            Assert.AreEqual(0, _state.EndGameVoteStatus.VotedToEnd.Count);
            Assert.IsTrue(_state.EndGameVoteStatus.RequiredVotes > 0);
        }

        [TestMethod]
        public void HandleCommand_VoteToEndGame_TracksVote()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand("p0"));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(_state.EndGameVoteStatus.VotedToEnd.Contains("p0"));
        }

        [TestMethod]
        public void HandleCommand_VoteToEndGame_RejectsDoubleVote()
        {
            var discussion = new DiscussionPhaseState();
            discussion.OnEnter(_context);

            discussion.HandleCommand(_context, new VoteToEndGameCommand("p0"));
            var result = discussion.HandleCommand(_context, new VoteToEndGameCommand("p0"));
            Assert.IsFalse(result.IsSuccess);
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
            Assert.IsInstanceOfType<VotePhaseState>(result.Value);
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
            Assert.IsInstanceOfType<VotePhaseState>(result.Value);
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
    }
}
