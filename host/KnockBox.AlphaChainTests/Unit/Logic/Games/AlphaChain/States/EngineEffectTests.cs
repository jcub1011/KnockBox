using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    /// <summary>
    /// Exercises the automated, rule-driven engine effects that replaced the abolished reaction
    /// tier — all through the real submit path: the Flak Cannon time-shave, the Bounty Hunter's
    /// leader drain, Tracer Round's end-letter hijack, and The Titanium Mirror's
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
                words, new FixedRandomNumberService(), new EngineEvaluator(), new ModifierCardFactory(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            // Tutorials off so the game starts directly in RoundState.
            state.UpdateSettings(s => s with { EnableTutorials = false });
            await engine.StartAsync(_host, state);
            DrainCountdown(engine, state);

            if (banned is { } b)
                state.Execute(() => state.BannedLetter = b);

            return (engine, state);
        }

        /// <summary>Ticks past the pre-round "Get Ready" countdown so the FSM lands in RoundState.</summary>
        private static void DrainCountdown(AlphaChainGameEngine engine, AlphaChainGameState state)
        {
            if (state.Phase == AlphaChainGamePhase.Countdown)
                engine.Tick(state.Context!, state.SubPhaseEndTime.AddSeconds(1));
        }

        private static void GiveModifier(AlphaChainGameState state, string playerId, string cardId) =>
            state.Execute(() => state.GamePlayers[playerId].EngineBay.Add(TestModifierCards.Create(cardId)));

        private static void SetScore(AlphaChainGameState state, string playerId, int score) =>
            state.Execute(() => state.GamePlayers[playerId].Score = score);

        // ── Flak Cannon (time-shave at higher-scored players) ────────────────

        [TestMethod]
        public async Task FlakCannon_GrantsZeroPoints_AndShavesHigherScoredPlayers()
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

            Assert.AreEqual(3, state.GamePlayers[submitter].Score, "Flak Cannon grants 0 points → just the length 3.");
            Assert.AreEqual(2, RoomStateProbe.QueuedTimePenalty(state, ahead), "Higher-scored player is shaved 2s.");
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

            Assert.AreEqual(0, RoomStateProbe.QueuedTimePenalty(state, behind), "A lower-scored player is not shaved.");
        }

        [TestMethod]
        public async Task QueuedTimeShave_IsDebitedFromTheVictimsNextClock()
        {
            // 2 players: the shaved player (index 1) IS the immediate next seat, so the queued shave
            // is applied to their freshly-armed clock the moment the turn advances to them — this
            // verifies the shave is actually consumed, not merely queued forever.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var victim = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "flak-cannon");
            SetScore(state, victim, 100); // ahead of the submitter → a Flak Cannon target

            var t0 = DateTimeOffset.UtcNow;
            await engine.SubmitWordAsync(submitter, "cat", state, t0);

            Assert.AreEqual(victim, state.TurnManager.CurrentPlayer, "Turn advanced to the shaved next-seat player.");
            Assert.AreEqual(0, RoomStateProbe.QueuedTimePenalty(state, victim), "Queued shave was consumed, not left pending.");

            // The freshly-armed clock is the base shot clock minus the 2s shave. Assert the duration
            // (tolerant of the sub-ms gap between t0 and the arm's own timestamp) — an un-shaved clock
            // would be 2s longer, far outside the tolerance.
            double armedSeconds = (state.PhaseEndTime - t0).TotalSeconds;
            Assert.AreEqual(state.Settings.ShotClockSeconds - 2, armedSeconds, 0.5,
                "Victim's freshly-armed clock is debited the 2s shave.");
        }

        // ── Double-letter era flag ───────────────────────────────────────────

        [TestMethod]
        public async Task DoubleLetterWord_FlagsSubmitterForTheEra()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("ee"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;

            await engine.SubmitWordAsync(submitter, "ee", state);

            Assert.IsTrue(RoomStateProbe.PlayedDoubleLetterWordThisEra(state, submitter),
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

            Assert.AreEqual(0, RoomStateProbe.QueuedTimePenalty(state, ahead), "Mirror blocks the shave.");
            Assert.AreEqual(2, RoomStateProbe.QueuedTimePenalty(state, submitter), "The shave is reflected at the caster.");
            Assert.AreEqual(0.9, RoomStateProbe.ShieldMultiplier(state, ahead), 1e-9, "Mirror decays 1.0 → 0.9 per block.");
        }

        [TestMethod]
        public async Task TitaniumMirror_SeedsShieldAtOne_AndFoldsInAsTimesOne()
        {
            // A fresh, undamaged Titanium Mirror seeds ShieldMultiplier at 1.0 and, since the card's
            // factor IS that multiplier, folds into the pipeline as a no-op ×1.0 — length 10 → 10.
            var (engine, state) = await StartGameAsync(new StubWordListService("basketball"), banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            GiveModifier(state, submitter, "titanium-mirror");

            Assert.AreEqual(1.0, RoomStateProbe.ShieldMultiplier(state, submitter), 1e-9, "Shield seeds at 1.0 at game start.");

            await engine.SubmitWordAsync(submitter, "basketball", state);

            Assert.AreEqual(10, state.GamePlayers[submitter].Score, "Undamaged mirror folds in as ×1.0 → length 10.");
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
            Assert.AreEqual(2, ranks[p0]);          // tie at 5 → earlier turn-order index ranks ahead
            Assert.AreEqual(3, ranks[p1]);          // later turn-order index of the tie
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
