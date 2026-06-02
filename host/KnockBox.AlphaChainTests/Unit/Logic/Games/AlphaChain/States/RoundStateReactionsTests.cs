using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    /// <summary>
    /// Exercises the auto-firing reaction system: Amnesty/Overtime through the real engine path,
    /// and the standings-driven offensive/board reactions plus Free Throw and Riposte through
    /// <see cref="ReactionResolver"/> with controlled standings.
    /// </summary>
    [TestClass]
    public class RoundStateReactionsTests
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

        private static void GiveReaction(AlphaChainGameState state, string playerId, string cardId) =>
            state.Execute(() => state.GamePlayers[playerId].ReactionHand.Add(ReactionLibrary.FindById(cardId)!));

        private static void SetScore(AlphaChainGameState state, string playerId, int score) =>
            state.Execute(() => state.GamePlayers[playerId].Score = score);

        private static int CountOf(AlphaChainGameState state, string playerId, ReactionTrigger trigger) =>
            state.GamePlayers[playerId].ReactionHand.Count(c => c.Trigger == trigger);

        // ── Amnesty (engine path) ───────────────────────────────────────────

        [TestMethod]
        public async Task Amnesty_AutoSuppressesTax_AndIsConsumed()
        {
            // Banned 'a' is inside "cat" → normally the Zero-Point Tax fires. A held Amnesty
            // auto-fires (no manual play) and suppresses it.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'a');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;
            GiveReaction(state, current, ReactionLibrary.AmnestyId);

            var outcome = await engine.SubmitWordAsync(current, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));

            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result);
            Assert.AreEqual(3, state.GamePlayers[current].Score);
            Assert.AreEqual(0, CountOf(state, current, ReactionTrigger.Amnesty), "Amnesty should be consumed.");
        }

        [TestMethod]
        public async Task Amnesty_DoesNotFire_OnCleanWord()
        {
            // No banned letter in "cat" (banned 'z') → no tax → Amnesty must not be wasted.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;
            GiveReaction(state, current, ReactionLibrary.AmnestyId);

            await engine.SubmitWordAsync(current, "cat", state);

            Assert.AreEqual(1, CountOf(state, current, ReactionTrigger.Amnesty), "Amnesty should be kept.");
        }

        // ── Overtime (engine Tick path) ─────────────────────────────────────

        [TestMethod]
        public async Task Overtime_ExtendsClock_OnExpiry_WithoutTimeoutOrAdvance()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;
            GiveReaction(state, current, ReactionLibrary.OvertimeId);

            // Force the shot clock to have expired.
            state.Execute(() => state.PhaseEndTime = DateTimeOffset.UtcNow.AddSeconds(-1));
            engine.Tick(state.Context!, DateTimeOffset.UtcNow);

            Assert.AreEqual(current, state.TurnManager.CurrentPlayer, "Turn must not advance.");
            Assert.AreEqual(0, state.GamePlayers[current].TurnTimeouts, "No timeout should be recorded.");
            Assert.AreEqual(0, CountOf(state, current, ReactionTrigger.Overtime), "Overtime should be consumed.");
            Assert.IsTrue(state.PhaseEndTime > DateTimeOffset.UtcNow, "Clock should be extended into the future.");
        }

        // ── Free Throw (resolver) ───────────────────────────────────────────

        [TestMethod]
        public async Task FreeThrow_ClearsRareRequiredLetter_AndIsConsumed()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;
            GiveReaction(state, current, ReactionLibrary.FreeThrowId);
            state.Execute(() => state.RequiredStartLetter = 'q');

            var notices = new List<ReactionEvent>();
            bool fired = false;
            state.Execute(() => fired = ReactionResolver.TryFreeThrow(state, notices));

            Assert.IsTrue(fired);
            Assert.IsNull(state.RequiredStartLetter);
            Assert.AreEqual(0, CountOf(state, current, ReactionTrigger.FreeThrow));
        }

        [TestMethod]
        public async Task FreeThrow_DoesNotFire_OnCommonLetter()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;
            GiveReaction(state, current, ReactionLibrary.FreeThrowId);
            state.Execute(() => state.RequiredStartLetter = 'c');

            var notices = new List<ReactionEvent>();
            bool fired = true;
            state.Execute(() => fired = ReactionResolver.TryFreeThrow(state, notices));

            Assert.IsFalse(fired);
            Assert.AreEqual('c', state.RequiredStartLetter);
            Assert.AreEqual(1, CountOf(state, current, ReactionTrigger.FreeThrow));
        }

        // ── Standings reactions (resolver) ──────────────────────────────────

        /// <summary>Sets pre-scores, captures the standings, then sets the submitter's post-score
        /// and runs the resolver — modelling "this word was just credited".</summary>
        private void ResolveSwing(AlphaChainGameState state, string submitterId, int finalScore,
            (string id, int pre, int post)[] scores, List<ReactionEvent> notices, int wordLength = 8)
        {
            foreach (var (id, pre, _) in scores) SetScore(state, id, pre);
            var preRanks = ReactionResolver.RankByScore(state);
            foreach (var (id, _, post) in scores) SetScore(state, id, post);
            state.Execute(() => ReactionResolver.ResolveAfterScore(
                state.Context!, submitterId, finalScore, wordLength, preRanks, notices));
        }

        [TestMethod]
        public async Task Frostbite_QueuesClockPenalty_WhenSubmitterOvertakesHolder()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id", p2 = "p2-id";
            GiveReaction(state, p1, ReactionLibrary.FrostbiteId);

            // p0 (submitter) climbs from last to first, overtaking p1.
            ResolveSwing(state, p0, finalScore: 20,
                [(p0, 0, 20), (p1, 10, 10), (p2, 5, 5)], new List<ReactionEvent>());

            Assert.AreEqual(ReactionLibrary.FrostbitePenaltySeconds, state.GamePlayers[p0].QueuedTimePenaltySeconds);
            Assert.AreEqual(0, CountOf(state, p1, ReactionTrigger.Frostbite), "Frostbite consumed.");
        }

        [TestMethod]
        public async Task Jinx_PlantsPersonalBan_WhenSubmitterTakesLead()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id", p2 = "p2-id";
            GiveReaction(state, p1, ReactionLibrary.JinxId);

            ResolveSwing(state, p0, finalScore: 20,
                [(p0, 0, 20), (p1, 10, 10), (p2, 5, 5)], new List<ReactionEvent>());

            Assert.IsNotNull(state.GamePlayers[p0].PersonalBannedLetter, "Submitter should be jinxed.");
            Assert.AreEqual(0, CountOf(state, p1, ReactionTrigger.Jinx), "Jinx consumed.");
        }

        [TestMethod]
        public async Task TollBooth_StealsPoints_WhenAheadSubmitterPostsLongWord()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id";
            GiveReaction(state, p1, ReactionLibrary.TollBoothId);

            // p0 is ahead and posts a 7+ letter word scoring 60; behind p1 tolls 20% = 12 points.
            ResolveSwing(state, p0, finalScore: 60,
                [(p0, 30, 60), (p1, 10, 10)], new List<ReactionEvent>(), wordLength: 8);

            Assert.AreEqual(48, state.GamePlayers[p0].Score, "Submitter loses 20% of the 60 just earned.");
            Assert.AreEqual(22, state.GamePlayers[p1].Score, "Toll Booth owner gains the 12 stolen.");
            Assert.AreEqual(0, CountOf(state, p1, ReactionTrigger.TollBooth), "TollBooth consumed.");
        }

        [TestMethod]
        public async Task TollBooth_DoesNotFire_OnShortWord()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id";
            GiveReaction(state, p1, ReactionLibrary.TollBoothId);

            // A 6-letter word is below the 7-letter threshold → no steal.
            ResolveSwing(state, p0, finalScore: 60,
                [(p0, 30, 90), (p1, 10, 10)], new List<ReactionEvent>(), wordLength: 6);

            Assert.AreEqual(90, state.GamePlayers[p0].Score, "No steal on a short word.");
            Assert.AreEqual(10, state.GamePlayers[p1].Score);
            Assert.AreEqual(1, CountOf(state, p1, ReactionTrigger.TollBooth), "TollBooth kept.");
        }

        [TestMethod]
        public async Task Windfall_DrawsCards_WhenHolderFallsToLast()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id", p2 = "p2-id";
            GiveReaction(state, p1, ReactionLibrary.WindfallId);

            // p0 vaults past everyone; p1 drops from 2nd to last.
            ResolveSwing(state, p0, finalScore: 20,
                [(p0, 0, 30), (p1, 6, 6), (p2, 8, 8)], new List<ReactionEvent>());

            // Started with 1 (Windfall), consumed it, drew WindfallDrawCount → net total.
            Assert.AreEqual(ReactionLibrary.WindfallDrawCount, state.GamePlayers[p1].ReactionHand.Count);
            Assert.AreEqual(0, CountOf(state, p1, ReactionTrigger.Windfall), "Windfall consumed.");
        }

        [TestMethod]
        public async Task Censor_ImposesBoardBan_WhenHolderFallsToLast()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id", p2 = "p2-id";
            GiveReaction(state, p1, ReactionLibrary.CensorId);

            ResolveSwing(state, p0, finalScore: 20,
                [(p0, 0, 30), (p1, 6, 6), (p2, 8, 8)], new List<ReactionEvent>());

            Assert.IsNotNull(state.CensorBannedLetter, "Censor should impose a board-wide ban.");
            Assert.AreEqual(0, CountOf(state, p1, ReactionTrigger.Censor));
        }

        [TestMethod]
        public async Task Windfall_FiresAtMostOncePerEra()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id", p2 = "p2-id";
            GiveReaction(state, p1, ReactionLibrary.WindfallId);

            // First drop to last → Windfall fires and sets the per-era guard.
            ResolveSwing(state, p0, finalScore: 20, [(p0, 0, 30), (p1, 6, 6), (p2, 8, 8)], new List<ReactionEvent>());
            Assert.IsTrue(state.GamePlayers[p1].WindfallFiredThisEra);

            // Give another Windfall and make p1 fall to last AGAIN this era → the guard blocks it.
            GiveReaction(state, p1, ReactionLibrary.WindfallId);
            ResolveSwing(state, p0, finalScore: 20, [(p0, 0, 30), (p1, 10, 10), (p2, 12, 12)], new List<ReactionEvent>());
            Assert.AreEqual(1, CountOf(state, p1, ReactionTrigger.Windfall), "Second Windfall must not fire this era.");
        }

        [TestMethod]
        public async Task FeedbackLoop_SilencesAttacker_WhenRiposteNegatesThem()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id";
            GiveReaction(state, p0, ReactionLibrary.RiposteId);       // submitter holds Riposte …
            GiveReaction(state, p0, ReactionLibrary.FeedbackLoopId);  // … and Feedback Loop
            GiveReaction(state, p1, ReactionLibrary.JinxId);          // opponent attacks with Jinx

            // p0 takes the lead → p1's Jinx fires; p0's Riposte negates it and Feedback Loop silences p1.
            ResolveSwing(state, p0, finalScore: 20, [(p0, 0, 20), (p1, 10, 10)], new List<ReactionEvent>());

            Assert.AreEqual(ReactionLibrary.FeedbackLoopSilenceSeconds, state.GamePlayers[p1].QueuedSilenceSeconds);
            Assert.AreEqual(0, CountOf(state, p0, ReactionTrigger.FeedbackLoop), "Feedback Loop consumed.");
        }

        // ── Riposte ─────────────────────────────────────────────────────────

        [TestMethod]
        public async Task Riposte_NegatesAndReflectsAttack_BackAtCaster()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id";
            GiveReaction(state, p0, ReactionLibrary.RiposteId); // submitter holds Riposte
            GiveReaction(state, p1, ReactionLibrary.JinxId);    // opponent attacks with Jinx

            // p0 takes the lead → p1's Jinx fires at p0, but p0's Riposte reflects it onto p1.
            ResolveSwing(state, p0, finalScore: 20,
                [(p0, 0, 20), (p1, 10, 10)], new List<ReactionEvent>());

            Assert.IsNull(state.GamePlayers[p0].PersonalBannedLetter, "Submitter should be protected.");
            Assert.IsNotNull(state.GamePlayers[p1].PersonalBannedLetter, "Caster should take the reflected Jinx.");
            Assert.AreEqual(0, CountOf(state, p0, ReactionTrigger.Riposte), "Riposte consumed.");
            Assert.AreEqual(0, CountOf(state, p1, ReactionTrigger.Jinx), "Jinx consumed.");
        }

        [TestMethod]
        public async Task Riposte_Reflection_IsNotItselfRiposted()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id";
            GiveReaction(state, p0, ReactionLibrary.RiposteId);
            GiveReaction(state, p1, ReactionLibrary.JinxId);
            GiveReaction(state, p1, ReactionLibrary.RiposteId); // caster ALSO holds a Riposte

            ResolveSwing(state, p0, finalScore: 20,
                [(p0, 0, 20), (p1, 10, 10)], new List<ReactionEvent>());

            // The reflected Jinx lands on p1 and is NOT blocked by p1's own Riposte (loop guard).
            Assert.IsNotNull(state.GamePlayers[p1].PersonalBannedLetter);
            Assert.AreEqual(1, CountOf(state, p1, ReactionTrigger.Riposte), "Caster's Riposte must be untouched.");
            Assert.IsNull(state.GamePlayers[p0].PersonalBannedLetter);
        }

        [TestMethod]
        public async Task Censor_ExemptsRiposteHolders_WithoutConsumingRiposte()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id", p2 = "p2-id";
            GiveReaction(state, p1, ReactionLibrary.CensorId); // p1 falls to last and imposes Censor
            GiveReaction(state, p0, ReactionLibrary.RiposteId); // p0 holds Riposte → exempt

            ResolveSwing(state, p0, finalScore: 20,
                [(p0, 0, 30), (p1, 6, 6), (p2, 8, 8)], new List<ReactionEvent>());

            Assert.IsNotNull(state.CensorBannedLetter);
            Assert.IsTrue(state.CensorExemptUserIds.Contains(p0), "Riposte holder should be exempt.");
            Assert.AreEqual(1, CountOf(state, p0, ReactionTrigger.Riposte), "Exemption must not consume Riposte.");
        }

        [TestMethod]
        public async Task SingleRiposte_BlocksFirstAttackOnly_WhenSubmitterEatsJinxAndFrostbite()
        {
            // One opponent holds BOTH attacks; the submitter holds a SINGLE Riposte. The fixed
            // firing order is Jinx (1a) → Frostbite (1b), so the Riposte reflects the Jinx and the
            // Frostbite then lands. Locks in the deterministic "blocks the first, not the worst"
            // priority documented on ReactionResolver.
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id";
            GiveReaction(state, p0, ReactionLibrary.RiposteId);
            GiveReaction(state, p1, ReactionLibrary.JinxId);
            GiveReaction(state, p1, ReactionLibrary.FrostbiteId);

            // p0 climbs from last to first, taking the lead (→ Jinx) AND overtaking p1 (→ Frostbite).
            ResolveSwing(state, p0, finalScore: 20,
                [(p0, 0, 20), (p1, 10, 10)], new List<ReactionEvent>());

            Assert.IsNull(state.GamePlayers[p0].PersonalBannedLetter, "Jinx was reflected, not landed on p0.");
            Assert.IsNotNull(state.GamePlayers[p1].PersonalBannedLetter, "Reflected Jinx landed on the caster.");
            Assert.AreEqual(ReactionLibrary.FrostbitePenaltySeconds, state.GamePlayers[p0].QueuedTimePenaltySeconds,
                "Frostbite lands because the single Riposte was already spent on the Jinx.");
            Assert.AreEqual(0, CountOf(state, p0, ReactionTrigger.Riposte), "Riposte consumed.");
            Assert.AreEqual(0, CountOf(state, p1, ReactionTrigger.Jinx), "Jinx consumed.");
            Assert.AreEqual(0, CountOf(state, p1, ReactionTrigger.Frostbite), "Frostbite consumed.");
        }

        [TestMethod]
        public async Task Jinx_FiresThroughSubmitWordPath_WhenSubmissionTakesLead()
        {
            // End-to-end through the real engine.SubmitWordAsync so HandleSubmitWord's pre/post-rank
            // capture and bounty-before-resolve ordering are exercised — not just the resolver in
            // isolation via ResolveSwing.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var opponent = state.GamePlayers.Keys.First(id => id != submitter);

            // Opponent leads by 2; the submitter's accepted "cat" (+3) takes the overall lead.
            SetScore(state, opponent, 2);
            GiveReaction(state, opponent, ReactionLibrary.JinxId);

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result);

            Assert.IsNotNull(state.GamePlayers[submitter].PersonalBannedLetter,
                "The submitter who took the lead should be jinxed via the real submit path.");
            Assert.AreEqual(0, CountOf(state, opponent, ReactionTrigger.Jinx), "Jinx consumed.");
        }

        [TestMethod]
        public async Task Overtime_PreventsElimination_InSurvivalMode()
        {
            // In Survival, a shot-clock expiry eliminates the current player — unless they hold
            // Overtime, which fires first and extends the clock. Covers the survival branch of the
            // Tick path that the existing Overtime test (non-survival) does not.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            state.UpdateSettings(s => s with { SurvivalMode = true });
            var current = state.TurnManager.CurrentPlayer!;
            GiveReaction(state, current, ReactionLibrary.OvertimeId);

            state.Execute(() => state.PhaseEndTime = DateTimeOffset.UtcNow.AddSeconds(-1));
            engine.Tick(state.Context!, DateTimeOffset.UtcNow);

            Assert.IsFalse(state.GamePlayers[current].IsEliminated,
                "Overtime should rescue the player from survival elimination.");
            Assert.AreEqual(current, state.TurnManager.CurrentPlayer, "Turn must not advance.");
            Assert.AreEqual(0, CountOf(state, current, ReactionTrigger.Overtime), "Overtime consumed.");
        }

        // ── Ranking ─────────────────────────────────────────────────────────

        [TestMethod]
        public async Task RankByScore_RanksHighestFirst_BreaksTiesByTurnOrder()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'z');
            using var _ = state;
            string p0 = "p0-id", p1 = "p1-id", p2 = "p2-id";
            SetScore(state, p0, 5);
            SetScore(state, p1, 5);
            SetScore(state, p2, 9);

            var ranks = ReactionResolver.RankByScore(state);

            Assert.AreEqual(1, ranks[p2]);          // highest score first
            Assert.IsTrue(ranks[p0] < ranks[p1]);   // tie broken by earlier turn-order index
        }
    }
}
