using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    /// <summary>
    /// Exercises the automated, rule-driven engine effects that replaced the abolished reaction
    /// tier — all through the real submit path: Flak Cannon and Scattershot time-shaves, the Bounty
    /// Hunter's leader drain, Tracer Round's end-letter hijack, and The Titanium Mirror's
    /// block-reflect-decay — plus the standings helpers on <see cref="EngineEffectResolver"/>.
    /// </summary>
    [TestClass]
    public class EngineEffectTests
    {
        private Mock<ILogger<AlphaChainGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<AlphaChainGameState>> _stateLoggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<AlphaChainGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<AlphaChainGameState>>();
            _host = UserFactory.Create("Host", "host1");
        }

        private static User MakePlayer(int index) => UserFactory.Create($"Player{index}", $"p{index}-id");

        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartGameAsync(
            StubWordListService words, int playerCount = 2, char? banned = null)
        {
            var engine = new AlphaChainGameEngine(
                words, new FixedRandomNumberService(), new ScoreCalculator(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            // Tutorials off so the game starts directly in RoundState.
            state.UpdateSettings(s => s with { EnableTutorials = false });
            await engine.StartAsync(_host, state);

            if (banned is { } b)
                state.Execute(() => state.BannedLetter = b);

            return (engine, state);
        }

        private static void GiveModifier(AlphaChainGameState state, string playerId, string cardId) =>
            state.Execute(() => state.GamePlayers[playerId].EngineBay.Add(ModifierLibrary.FindById(cardId)!));

        private static void SetScore(AlphaChainGameState state, string playerId, int score) =>
            state.Execute(() => state.GamePlayers[playerId].Score = score);

        // ── Flak Cannon (time-shave at higher-scored players) ────────────────

        [TestMethod]
        public async Task FlakCannon_AddsFlatFive_AndShavesHigherScoredPlayers()
        {
            // 3 players so the shaved player (index 2) is not the immediate next seat — otherwise its
            // queued shave would be debited into the clock (and cleared) before we can observe it.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var ahead = state.TurnManager.TurnOrder[2];

            GiveModifier(state, submitter, "flak-cannon");
            SetScore(state, ahead, 100); // clearly ahead of the submitter's small score

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(8, state.GamePlayers[submitter].Score, "length 3 + flat 5.");
            Assert.AreEqual(2, state.GamePlayers[ahead].QueuedTimePenaltySeconds, "Higher-scored player is shaved 2s.");
        }

        [TestMethod]
        public async Task FlakCannon_DoesNotShaveLowerScoredPlayers()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var behind = state.TurnManager.TurnOrder[2];

            GiveModifier(state, submitter, "flak-cannon");
            SetScore(state, submitter, 100); // submitter is clearly ahead of everyone
            SetScore(state, behind, 0);

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(0, state.GamePlayers[behind].QueuedTimePenaltySeconds, "A lower-scored player is not shaved.");
        }

        // ── Scattershot (time-shave at double-letter opponents) ──────────────

        [TestMethod]
        public async Task Scattershot_ShavesOpponentsWhoPlayedDoubleLetterThisEra()
        {
            // doubler is index 2 (not the immediate next seat) so its queued shave survives to assert.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var clean = state.TurnManager.TurnOrder[1];
            var doubler = state.TurnManager.TurnOrder[2];

            GiveModifier(state, submitter, "scattershot");
            state.Execute(() => state.GamePlayers[doubler].PlayedDoubleLetterWordThisEra = true);

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(3, state.GamePlayers[doubler].QueuedTimePenaltySeconds, "Double-letter player is shaved 3s.");
            Assert.AreEqual(0, state.GamePlayers[clean].QueuedTimePenaltySeconds, "A clean player is not shaved.");
        }

        [TestMethod]
        public async Task DoubleLetterWord_FlagsSubmitterForScattershotTargeting()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("ee"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;

            await engine.SubmitWordAsync(submitter, "ee", state);

            Assert.IsTrue(state.GamePlayers[submitter].PlayedDoubleLetterWordThisEra,
                "A word with a double letter flags the player for the rest of the era.");
        }

        // ── The Bounty Hunter (leader short-word drain) ──────────────────────

        [TestMethod]
        public async Task BountyHunter_DocksMarkedLeader_OnShortWord()
        {
            // Fresh game: all scores 0, so the round leader is the opening player (the submitter).
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var leader = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];
            Assert.AreEqual(leader, state.RoundLeaderUserId, "Opening player is the marked leader.");

            GiveModifier(state, owner, "bounty-hunter");

            await engine.SubmitWordAsync(leader, "cat", state); // 3 letters < 6 → docked 30

            Assert.AreEqual(0, state.GamePlayers[leader].Score, "Leader's 3 is docked 30 → floored at 0.");
        }

        [TestMethod]
        public async Task BountyHunter_DoesNotDock_OnLongEnoughWord()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("bridge"), playerCount: 2, banned: 'z');
            using var _ = state;
            var leader = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, owner, "bounty-hunter");

            await engine.SubmitWordAsync(leader, "bridge", state); // 6 letters → safe

            Assert.AreEqual(6, state.GamePlayers[leader].Score, "A 6-letter word meets the threshold — no dock.");
        }

        // ── Tracer Round (end-letter hijack) ─────────────────────────────────

        [TestMethod]
        public async Task TracerRound_BansNextPlayersStart_WithTheWordEndingLetter()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var next = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "tracer-round");

            await engine.SubmitWordAsync(submitter, "cat", state); // ends 't'

            Assert.AreEqual('t', state.GamePlayers[next].PersonalBannedLetter, "Next player is banned from 't'.");
        }

        // ── The Titanium Mirror (block, reflect, decay) ──────────────────────

        [TestMethod]
        public async Task TitaniumMirror_BlocksAndReflectsTimeShave_AndDecays()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var ahead = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "flak-cannon");
            GiveModifier(state, ahead, "titanium-mirror");
            SetScore(state, ahead, 100); // ahead → a Flak Cannon target

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(0, state.GamePlayers[ahead].QueuedTimePenaltySeconds, "Mirror blocks the shave.");
            Assert.AreEqual(2, state.GamePlayers[submitter].QueuedTimePenaltySeconds, "The shave is reflected at the caster.");
            Assert.AreEqual(0.9, state.GamePlayers[ahead].ShieldMultiplier, 1e-9, "Mirror decays 1.0 → 0.9 per block.");
        }

        // ── Standings helpers ────────────────────────────────────────────────

        [TestMethod]
        public async Task RankByScore_RanksHighestFirst_BreaksTiesByTurnOrder()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id", p2 = "p2-id";
            SetScore(state, p0, 5);
            SetScore(state, p1, 5);
            SetScore(state, p2, 9);

            var ranks = EngineEffectResolver.RankByScore(state);

            Assert.AreEqual(1, ranks[p2]);          // highest score first
            Assert.IsTrue(ranks[p0] < ranks[p1]);   // tie broken by earlier turn-order index
        }

        [TestMethod]
        public async Task LeaderUserId_IsHighestScorer_TiesByTurnOrder()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id";
            SetScore(state, p1, 7);

            Assert.AreEqual(p1, EngineEffectResolver.LeaderUserId(state));

            // Tie at the top → earliest turn order wins.
            SetScore(state, p0, 7);
            Assert.AreEqual(p0, EngineEffectResolver.LeaderUserId(state));
        }
    }
}
