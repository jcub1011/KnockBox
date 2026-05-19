using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Codeword.Services.Logic.Games.FSM;
using KnockBox.Codeword.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.Codeword.Tests.Unit.Logic.Games.Codeword
{
    /// <summary>
    /// Tests for player-leave handling across different game phases.
    /// </summary>
    [TestClass]
    public class CodewordGameEnginePlayerLeftTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger<CodewordGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<CodewordGameState>> _stateLoggerMock = default!;
        private CodewordGameEngine _engine = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            int callCount = 0;
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int max, RandomType _) => { callCount++; return callCount % 2 == 0 ? 1 % max : 0; });
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int min, int max, RandomType _) => min);

            _engineLoggerMock = new Mock<ILogger<CodewordGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<CodewordGameState>>();

            _host = UserFactory.Create("Host", "host-id");

            _engine = new CodewordGameEngine(
                _randomMock.Object,
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        private User MakePlayer(int index) => UserFactory.Create($"Player{index}", $"p{index}-id");

        private async Task<CodewordGameState> CreateStartedGameAsync(int playerCount = 5)
        {
            var result = await _engine.CreateStateAsync(_host);
            var state = (CodewordGameState)result.Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));
            await _engine.StartAsync(_host, state);
            return state;
        }

        [TestMethod]
        public async Task PlayerLeft_DuringCluePhase_CurrentClueGiver_AdvancesToNext()
        {
            using var state = await CreateStartedGameAsync(5);
            var context = state.Context!;

            // Advance to CluePhase by ticking past setup timeout.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddSeconds(10));
            Assert.AreEqual(CodewordGamePhase.CluePhase, state.Phase);

            // Identify the current clue giver.
            string currentClueGiverId = state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex];

            // Remove the current clue giver.
            _engine.HandlePlayerLeft(UserFactory.Create("dummy", currentClueGiverId), state);

            // Game should still be in CluePhase (re-entered) and the index should point to an alive player.
            Assert.AreEqual(CodewordGamePhase.CluePhase, state.Phase);
            string newClueGiverId = state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex];
            var newPlayer = context.GetPlayer(newClueGiverId);
            Assert.IsNotNull(newPlayer);
            Assert.IsFalse(newPlayer.IsEliminated);
        }

        [TestMethod]
        public async Task PlayerLeft_DuringDiscussionPhase_VoidsVotesAndRechecks()
        {
            using var state = await CreateStartedGameAsync(5);
            var context = state.Context!;

            // Advance to CluePhase.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddSeconds(10));
            Assert.AreEqual(CodewordGamePhase.CluePhase, state.Phase);

            // Submit clues for all alive players to advance to Discussion.
            var alivePlayers = context.GetAlivePlayers();
            Assert.HasCount(5, alivePlayers, "Expected 5 alive players.");
            string[] clues = ["wave", "splash", "tide", "fish", "coral"];
            for (int i = 0; i < alivePlayers.Count; i++)
            {
                string currentPlayerId = state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex];
                _engine.SubmitClue(UserFactory.Create("dummy", currentPlayerId), state, clues[i]);
            }

            // Should now be in Discussion (which includes inline voting).
            Assert.AreEqual(CodewordGamePhase.Discussion, state.Phase);

            // Have a player select a vote target (inline voting in discussion phase).
            alivePlayers = context.GetAlivePlayers();
            string leavingPlayerId = alivePlayers[0].PlayerId;
            string voterId = alivePlayers[1].PlayerId;

            // Cast a vote for the player who will leave (select target, don't lock in).
            _engine.CastVote(UserFactory.Create("dummy", voterId), state, leavingPlayerId);

            var voterState = context.GetPlayer(voterId)!;
            Assert.AreEqual(leavingPlayerId, voterState.VoteTargetId);

            // Player leaves.
            _engine.HandlePlayerLeft(UserFactory.Create("dummy", leavingPlayerId), state);

            // Vote targeting the leaving player should be voided.
            Assert.IsFalse(voterState.HasVoted, "Vote should be voided when target leaves.");
            Assert.IsNull(voterState.VoteTargetId, "VoteTargetId should be cleared.");

            // Phase should remain Discussion (not enough players gone to end game).
            Assert.AreEqual(CodewordGamePhase.Discussion, state.Phase);
        }

        [TestMethod]
        public async Task PlayerLeft_InsiderLeaves_AgentsWinCheck()
        {
            using var state = await CreateStartedGameAsync(4);
            var context = state.Context!;

            // Find the Insider player(s) and make it so removing them (plus one more)
            // brings us down to ≤2 players, triggering a win check.
            var insiders = state.GamePlayers.Values.Where(p => p.Role == Role.Insider).ToList();
            var agents = state.GamePlayers.Values.Where(p => p.Role == Role.Agent).ToList();

            // With 4 players: 3 Agent, 1 Insider.
            // Remove 2 agents to get to 2 remaining (1 Agent + 1 Insider).
            _engine.HandlePlayerLeft(UserFactory.Create("dummy", agents[0].PlayerId), state);
            _engine.HandlePlayerLeft(UserFactory.Create("dummy", agents[1].PlayerId), state);

            // Should transition to GameOver with ≤2 remaining.
            Assert.AreEqual(CodewordGamePhase.GameOver, state.Phase);
            Assert.IsNotNull(state.WinResult);
            Assert.IsTrue(state.WinResult.GameOver);
            // Insider should win (Insider alive, no Informant).
            Assert.AreEqual(Role.Insider, state.WinResult.WinningTeam);
        }

        [TestMethod]
        public async Task PlayerLeft_AllLeave_TransitionsToGameOver()
        {
            using var state = await CreateStartedGameAsync(4);

            // Remove all players.
            for (int i = 0; i < 4; i++)
            {
                _engine.HandlePlayerLeft(MakePlayer(i), state);
            }

            Assert.AreEqual(CodewordGamePhase.GameOver, state.Phase);
        }

        [TestMethod]
        public async Task PlayerLeft_DuringCluePhase_PreservesOtherPlayersSubmittedClues()
        {
            // Regression: re-entering CluePhaseState after a leaver used to call
            // ResetEliminationCycleState, wiping HasSubmittedClue across every alive
            // player. Players who already submitted would be re-prompted forever.
            using var state = await CreateStartedGameAsync(5);
            var context = state.Context!;

            // Advance to CluePhase.
            _engine.Tick(context, DateTimeOffset.UtcNow.AddSeconds(10));
            Assert.AreEqual(CodewordGamePhase.CluePhase, state.Phase);

            // First two players submit clues.
            string[] clues = ["wave", "splash"];
            for (int i = 0; i < 2; i++)
            {
                string currentPlayerId = state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex];
                _engine.SubmitClue(UserFactory.Create("dummy", currentPlayerId), state, clues[i]);
            }

            // Capture the players who submitted.
            var submittedIds = context.GetAlivePlayers()
                .Where(p => p.HasSubmittedClue)
                .Select(p => p.PlayerId)
                .ToList();
            Assert.HasCount(2, submittedIds, "Expected 2 players to have submitted.");

            // The next player in the turn order (the active clue giver) leaves.
            string activeClueGiverId = state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex];
            _engine.HandlePlayerLeft(UserFactory.Create("dummy", activeClueGiverId), state);

            // Phase must still be CluePhase (not Setup, not Reveal).
            Assert.AreEqual(CodewordGamePhase.CluePhase, state.Phase);

            // The two players who already submitted must still be marked as submitted —
            // before the fix, ResetEliminationCycleState would have wiped these flags.
            foreach (var id in submittedIds)
            {
                var p = context.GetPlayer(id);
                Assert.IsNotNull(p);
                Assert.IsTrue(p.HasSubmittedClue, $"Player {id} lost their submitted-clue flag after a leaver.");
            }
        }

        [TestMethod]
        public async Task PlayerLeft_DuringCluePhase_AllOthersSubmitted_AdvancesToDiscussion()
        {
            // If the active clue-giver leaves while every other alive player has
            // already submitted, fast-forward to DiscussionPhase rather than getting
            // stuck on a non-existent index.
            using var state = await CreateStartedGameAsync(5);
            var context = state.Context!;

            _engine.Tick(context, DateTimeOffset.UtcNow.AddSeconds(10));
            Assert.AreEqual(CodewordGamePhase.CluePhase, state.Phase);

            // Submit clues for the first 4 players in turn order.
            string[] clues = ["wave", "splash", "tide", "fish"];
            for (int i = 0; i < 4; i++)
            {
                string currentPlayerId = state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex];
                _engine.SubmitClue(UserFactory.Create("dummy", currentPlayerId), state, clues[i]);
            }

            // The 5th player is now the active clue-giver. They leave.
            string activeClueGiverId = state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex];
            _engine.HandlePlayerLeft(UserFactory.Create("dummy", activeClueGiverId), state);

            // No eligible un-submitted alive player remains → advance to Discussion.
            Assert.AreEqual(CodewordGamePhase.Discussion, state.Phase);
        }

        [TestMethod]
        public async Task PlayerLeft_DuringDiscussion_AdjustsAliveCount()
        {
            using var state = await CreateStartedGameAsync(5);
            var context = state.Context!;

            // Manually set phase to Discussion to simulate being in that phase.
            state.SetPhase(CodewordGamePhase.Discussion);

            int aliveCountBefore = context.GetAlivePlayerCount();
            string leavingPlayerId = state.TurnManager.TurnOrder[0];

            _engine.HandlePlayerLeft(UserFactory.Create("dummy", leavingPlayerId), state);

            int aliveCountAfter = context.GetAlivePlayerCount();
            Assert.AreEqual(aliveCountBefore - 1, aliveCountAfter);
        }

        [TestMethod]
        public async Task PlayerLeft_DuringContinueOrEndRound_RecomputesMajority()
        {
            using var state = await CreateStartedGameAsync(4);
            var context = state.Context!;

            _engine.Tick(context, DateTimeOffset.UtcNow.AddSeconds(10));
            context.Fsm.TransitionTo(context, new ContinueOrEndRoundPhaseState());
            Assert.AreEqual(CodewordGamePhase.ContinueOrEndRound, state.Phase);
            Assert.AreEqual(3, state.EndGameVoteStatus.RequiredVotes);

            // p0 votes end (1 of 3 — not majority).
            string p0Id = state.TurnManager.TurnOrder[0];
            _engine.VoteContinueOrEndRound(UserFactory.Create("dummy", p0Id), state, voteToEnd: true);
            Assert.HasCount(1, state.EndGameVoteStatus.VotedToEnd);

            // p1 leaves without voting.
            string p1Id = state.TurnManager.TurnOrder[1];
            _engine.HandlePlayerLeft(UserFactory.Create("dummy", p1Id), state);

            // 3 alive remaining → required = (3/2)+1 = 2. VotedToEnd = {p0} (1) < 2,
            // and p2/p3 still un-voted → stay in ContinueOrEndRound.
            Assert.AreEqual(CodewordGamePhase.ContinueOrEndRound, state.Phase);
            Assert.AreEqual(2, state.EndGameVoteStatus.RequiredVotes);
            Assert.HasCount(1, state.EndGameVoteStatus.VotedToEnd);
        }

        [TestMethod]
        public async Task PlayerLeft_DuringContinueOrEndRound_LeaverTipsMajority_TransitionsToGameOver()
        {
            using var state = await CreateStartedGameAsync(4);
            var context = state.Context!;

            _engine.Tick(context, DateTimeOffset.UtcNow.AddSeconds(10));
            context.Fsm.TransitionTo(context, new ContinueOrEndRoundPhaseState());

            string p0Id = state.TurnManager.TurnOrder[0];
            string p1Id = state.TurnManager.TurnOrder[1];
            string p2Id = state.TurnManager.TurnOrder[2];

            // p0 + p1 vote end (2 of 3 — not majority yet).
            _engine.VoteContinueOrEndRound(UserFactory.Create("dummy", p0Id), state, voteToEnd: true);
            _engine.VoteContinueOrEndRound(UserFactory.Create("dummy", p1Id), state, voteToEnd: true);
            Assert.AreEqual(CodewordGamePhase.ContinueOrEndRound, state.Phase);

            // p2 leaves without voting. New required = (3/2)+1 = 2; VotedToEnd already 2 → GameOver.
            _engine.HandlePlayerLeft(UserFactory.Create("dummy", p2Id), state);

            Assert.AreEqual(CodewordGamePhase.GameOver, state.Phase);
            Assert.IsNotNull(state.WinResult);
            Assert.IsTrue(state.WinResult.GameOver);
        }

        [TestMethod]
        public async Task PlayerLeft_DuringContinueOrEndRound_AllRemainingVoted_AdvancesToCluePhase()
        {
            using var state = await CreateStartedGameAsync(4);
            var context = state.Context!;

            _engine.Tick(context, DateTimeOffset.UtcNow.AddSeconds(10));
            context.Fsm.TransitionTo(context, new ContinueOrEndRoundPhaseState());

            string p0Id = state.TurnManager.TurnOrder[0];
            string p1Id = state.TurnManager.TurnOrder[1];
            string p2Id = state.TurnManager.TurnOrder[2];
            string p3Id = state.TurnManager.TurnOrder[3];

            // p0, p1, p2 vote continue. p3 hasn't voted.
            _engine.VoteContinueOrEndRound(UserFactory.Create("dummy", p0Id), state, voteToEnd: false);
            _engine.VoteContinueOrEndRound(UserFactory.Create("dummy", p1Id), state, voteToEnd: false);
            _engine.VoteContinueOrEndRound(UserFactory.Create("dummy", p2Id), state, voteToEnd: false);
            Assert.AreEqual(CodewordGamePhase.ContinueOrEndRound, state.Phase);

            // p3 leaves. All 3 remaining alive have already voted → no majority for end → CluePhase.
            _engine.HandlePlayerLeft(UserFactory.Create("dummy", p3Id), state);

            Assert.AreEqual(CodewordGamePhase.CluePhase, state.Phase);
        }
    }
}
