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
    public class ContinueOrEndRoundPhaseStateTests
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

            var host = UserFactory.Create("Host", "host-id");
            _state = new CodewordGameState(host, _stateLogger.Object);
            _context = new CodewordGameContext(_state, _rng.Object, _logger.Object);

            // 4 alive players (one Insider, three Agents) — enough to vote and not auto-end.
            AddPlayer("p0", "Player 0", Role.Agent, "Ocean");
            AddPlayer("p1", "Player 1", Role.Agent, "Ocean");
            AddPlayer("p2", "Player 2", Role.Insider, "Lake");
            AddPlayer("p3", "Player 3", Role.Agent, "Ocean");
            _state.CurrentWordPair = ["Ocean", "Lake"];
        }

        private void AddPlayer(string id, string name, Role role, string? secretWord)
        {
            _state.GamePlayers[id] = new CodewordPlayerState
            {
                PlayerId = id,
                DisplayName = name,
                Role = role,
                SecretWord = secretWord
            };
            _state.TurnManager.TurnOrder.Add(id);
        }

        [TestMethod]
        public void OnEnter_SetsPhaseAndRequiredMajority()
        {
            var phase = new ContinueOrEndRoundPhaseState();
            phase.OnEnter(_context);

            Assert.AreEqual(CodewordGamePhase.ContinueOrEndRound, _state.Phase);
            // 4 alive → majority requires 3.
            Assert.AreEqual(3, _state.EndGameVoteStatus.RequiredVotes);
            Assert.IsEmpty(_state.EndGameVoteStatus.VotedToEnd);
            foreach (var p in _context.GetAlivePlayers())
                Assert.IsNull(p.ContinueOrEndVote);
        }

        [TestMethod]
        public void HandleCommand_VoteContinue_RecordsButDoesNotAdvance()
        {
            var phase = new ContinueOrEndRoundPhaseState();
            phase.OnEnter(_context);

            var result = phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p0", VoteToEnd: false));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
            Assert.IsFalse(_state.GamePlayers["p0"].ContinueOrEndVote);
            Assert.IsEmpty(_state.EndGameVoteStatus.VotedToEnd);
        }

        [TestMethod]
        public void HandleCommand_VoteEnd_TogglesEndStatus()
        {
            var phase = new ContinueOrEndRoundPhaseState();
            phase.OnEnter(_context);

            phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p0", VoteToEnd: true));
            Assert.IsTrue(_state.GamePlayers["p0"].ContinueOrEndVote);
            Assert.Contains("p0", _state.EndGameVoteStatus.VotedToEnd);

            // Second click on same option rescinds.
            phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p0", VoteToEnd: true));
            Assert.IsNull(_state.GamePlayers["p0"].ContinueOrEndVote);
            Assert.DoesNotContain("p0", _state.EndGameVoteStatus.VotedToEnd);
        }

        [TestMethod]
        public void HandleCommand_FlipFromEndToContinue_RemovesEndStatus()
        {
            var phase = new ContinueOrEndRoundPhaseState();
            phase.OnEnter(_context);

            phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p0", VoteToEnd: true));
            phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p0", VoteToEnd: false));

            Assert.IsFalse(_state.GamePlayers["p0"].ContinueOrEndVote);
            Assert.DoesNotContain("p0", _state.EndGameVoteStatus.VotedToEnd);
        }

        [TestMethod]
        public void HandleCommand_EliminatedPlayer_Rejected()
        {
            _state.GamePlayers["p0"].IsEliminated = true;

            var phase = new ContinueOrEndRoundPhaseState();
            phase.OnEnter(_context);

            var result = phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p0", VoteToEnd: true));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_AllVoteContinue_TransitionsToCluePhase()
        {
            var phase = new ContinueOrEndRoundPhaseState();
            phase.OnEnter(_context);

            phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p0", VoteToEnd: false));
            phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p1", VoteToEnd: false));
            phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p2", VoteToEnd: false));
            var result = phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p3", VoteToEnd: false));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<CluePhaseState>(result.Value);
        }

        [TestMethod]
        public void HandleCommand_MajorityEnd_TransitionsToGameOver()
        {
            var phase = new ContinueOrEndRoundPhaseState();
            phase.OnEnter(_context);

            phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p0", VoteToEnd: true));
            phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p1", VoteToEnd: true));
            // Third "end" vote reaches the majority of 3 — game over.
            var result = phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p2", VoteToEnd: true));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<GameOverState>(result.Value);
            Assert.IsNotNull(_state.WinResult);
            Assert.IsTrue(_state.WinResult.GameOver);
        }

        [TestMethod]
        public void Tick_BeforeTimeout_ReturnsNull()
        {
            var phase = new ContinueOrEndRoundPhaseState();
            phase.OnEnter(_context);

            var result = phase.Tick(_context, DateTimeOffset.UtcNow);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void Tick_Timeout_DefaultsNonVotersToContinue()
        {
            var phase = new ContinueOrEndRoundPhaseState();
            phase.OnEnter(_context);

            // Only one "end" vote — not majority.
            phase.HandleCommand(_context, new ContinueOrEndRoundVoteCommand("p0", VoteToEnd: true));

            var result = phase.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<CluePhaseState>(result.Value);
            // Non-voters were defaulted to continue.
            Assert.IsFalse(_state.GamePlayers["p1"].ContinueOrEndVote);
            Assert.IsFalse(_state.GamePlayers["p2"].ContinueOrEndVote);
            Assert.IsFalse(_state.GamePlayers["p3"].ContinueOrEndVote);
        }

    }
}
