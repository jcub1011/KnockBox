using KnockBox.ConsultTheCard.Services.Logic.Games;
using KnockBox.ConsultTheCard.Services.Logic.Games.FSM;
using KnockBox.ConsultTheCard.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.ConsultTheCard.Services.State.Games;
using KnockBox.ConsultTheCard.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.ConsultTheCard.Tests.Unit.Logic.Games.ConsultTheCard
{
    /// <summary>
    /// Tests for player-leave handling across different game phases.
    /// </summary>
    [TestClass]
    public class ConsultTheCardGameEnginePlayerLeftTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger<ConsultTheCardGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<ConsultTheCardGameState>> _stateLoggerMock = default!;
        private ConsultTheCardGameEngine _engine = default!;
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

            _engineLoggerMock = new Mock<ILogger<ConsultTheCardGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<ConsultTheCardGameState>>();

            _host = new User("Host", "host-id");

            _engine = new ConsultTheCardGameEngine(
                _randomMock.Object,
                _engineLoggerMock.Object,
                _stateLoggerMock.Object);
        }

        private User MakePlayer(int index) => new($"Player{index}", $"p{index}-id");

        private async Task<ConsultTheCardGameState> CreateStartedGameAsync(int playerCount = 5)
        {
            var result = await _engine.CreateStateAsync(_host);
            var state = (ConsultTheCardGameState)result.Value!;
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
            Assert.AreEqual(ConsultTheCardGamePhase.CluePhase, state.Phase);

            // Identify the current clue giver.
            string currentClueGiverId = state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex];

            // Remove the current clue giver.
            _engine.HandlePlayerLeft(new User("dummy", currentClueGiverId), state);

            // Game should still be in CluePhase (re-entered) and the index should point to an alive player.
            Assert.AreEqual(ConsultTheCardGamePhase.CluePhase, state.Phase);
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
            Assert.AreEqual(ConsultTheCardGamePhase.CluePhase, state.Phase);

            // Submit clues for all alive players to advance to Discussion.
            var alivePlayers = context.GetAlivePlayers();
            Assert.AreEqual(5, alivePlayers.Count, "Expected 5 alive players.");
            string[] clues = ["wave", "splash", "tide", "fish", "coral"];
            for (int i = 0; i < alivePlayers.Count; i++)
            {
                string currentPlayerId = state.TurnManager.TurnOrder[state.TurnManager.CurrentPlayerIndex];
                _engine.SubmitClue(new User("dummy", currentPlayerId), state, clues[i]);
            }

            // Should now be in Discussion (which includes inline voting).
            Assert.AreEqual(ConsultTheCardGamePhase.Discussion, state.Phase);

            // Have a player select a vote target (inline voting in discussion phase).
            alivePlayers = context.GetAlivePlayers();
            string leavingPlayerId = alivePlayers[0].PlayerId;
            string voterId = alivePlayers[1].PlayerId;

            // Cast a vote for the player who will leave (select target, don't lock in).
            _engine.CastVote(new User("dummy", voterId), state, leavingPlayerId);

            var voterState = context.GetPlayer(voterId)!;
            Assert.AreEqual(leavingPlayerId, voterState.VoteTargetId);

            // Player leaves.
            _engine.HandlePlayerLeft(new User("dummy", leavingPlayerId), state);

            // Vote targeting the leaving player should be voided.
            Assert.IsFalse(voterState.HasVoted, "Vote should be voided when target leaves.");
            Assert.IsNull(voterState.VoteTargetId, "VoteTargetId should be cleared.");

            // Phase should remain Discussion (not enough players gone to end game).
            Assert.AreEqual(ConsultTheCardGamePhase.Discussion, state.Phase);
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
            _engine.HandlePlayerLeft(new User("dummy", agents[0].PlayerId), state);
            _engine.HandlePlayerLeft(new User("dummy", agents[1].PlayerId), state);

            // Should transition to GameOver with ≤2 remaining.
            Assert.AreEqual(ConsultTheCardGamePhase.GameOver, state.Phase);
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

            Assert.AreEqual(ConsultTheCardGamePhase.GameOver, state.Phase);
        }

        [TestMethod]
        public async Task PlayerLeft_DuringDiscussion_AdjustsAliveCount()
        {
            using var state = await CreateStartedGameAsync(5);
            var context = state.Context!;

            // Manually set phase to Discussion to simulate being in that phase.
            state.SetPhase(ConsultTheCardGamePhase.Discussion);

            int aliveCountBefore = context.GetAlivePlayerCount();
            string leavingPlayerId = state.TurnManager.TurnOrder[0];

            _engine.HandlePlayerLeft(new User("dummy", leavingPlayerId), state);

            int aliveCountAfter = context.GetAlivePlayerCount();
            Assert.AreEqual(aliveCountBefore - 1, aliveCountAfter);
        }
    }
}
