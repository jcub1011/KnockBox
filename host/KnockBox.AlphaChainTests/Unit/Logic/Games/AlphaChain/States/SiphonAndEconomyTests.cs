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
    /// Exercises the tax/ban economy cards through the real submit path: the Tax Collector siphon,
    /// The Toll Booth (card-ban siphon), The IRS Agent (0-point bounty suppression), Bait &amp;
    /// Switch (forced next-player ban), and Roulette Wheel (reward + self-tax).
    /// </summary>
    [TestClass]
    public class SiphonAndEconomyTests
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
            StubWordListService words, int playerCount, char banned)
        {
            var engine = new AlphaChainGameEngine(
                words, new FixedRandomNumberService(), new EngineEvaluator(), new ModifierCardFactory(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            state.UpdateSettings(s => s with { EnableTutorials = false });
            await engine.StartAsync(_host, state);
            DrainCountdown(engine, state);
            state.Execute(() => state.BannedLetter = banned);
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

        private static void SetCardBan(AlphaChainGameState state, string playerId, string cardId, char letter) =>
            state.Execute(() => state.GamePlayers[playerId].CardBannedLetters[TestModifierCards.ToId(cardId)] = letter);

        // ── Tax Collector ───────────────────────────────────────────────────

        [TestMethod]
        public async Task TaxCollector_CollectsHalfTheWouldBeScore()
        {
            // "cat" + Anchor → would-be 13 (3 + 10); banned 'a' taxes it. Tax Collector owner takes 50%.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "anchor");
            GiveModifier(state, owner, TestModifierCards.TaxCollectorId);

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(0, state.GamePlayers[submitter].Score);
            Assert.AreEqual(7, state.GamePlayers[owner].Score, "Tax Collector collects round(13 × 0.5) = 7 (half-up).");
        }

        // ── The Toll Booth (card-ban siphon) ────────────────────────────────

        [TestMethod]
        public async Task TollBooth_MintsCut_WhenOpponentUsesRolledLetter()
        {
            // Clean word (banned 'z' absent from "cat"); submitter Anchor → earns 13 (3 + 10). Owner's
            // Toll Booth letter 't' is in "cat" → owner minted round(13 × 0.2) = 3; submitter keeps 13.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "anchor");
            GiveModifier(state, owner, "toll-booth");
            SetCardBan(state, owner, "toll-booth", 't');

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(13, state.GamePlayers[submitter].Score, "Submitter keeps their full score.");
            Assert.AreEqual(3, state.GamePlayers[owner].Score, "Owner is minted round(13 × 0.2) = 3 (half-up).");
        }

        [TestMethod]
        public async Task TollBooth_PaysNothing_OnTaxedWord()
        {
            // Banned 'a' taxes "cat" to 0 → no earned points → no Smuggler mint even though 't' is used.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, owner, "toll-booth");
            SetCardBan(state, owner, "toll-booth", 't');

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(0, state.GamePlayers[owner].Score);
        }

        // ── The IRS Agent (0-point bounty suppression) ──────────────────────

        [TestMethod]
        public async Task IrsAgent_ScoresZero_AndSuppressesBounty()
        {
            // Submitter holds only The IRS Agent; banned 'a' taxes "cat". The IRS Agent grants 0
            // points (it is a utility slot, no salvage) but denies the opposing Tax Collector its cut.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "irs");
            GiveModifier(state, owner, TestModifierCards.TaxCollectorId);

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.AcceptedZeroPointTax>(result, "0-point tax reads as a taxed accept.");

            Assert.AreEqual(0, state.GamePlayers[submitter].Score, "The IRS Agent grants 0 points.");
            Assert.AreEqual(0, state.GamePlayers[owner].Score, "Tax Collector bounty is suppressed by The IRS Agent.");
        }

        // ── Bait & Switch (forced next-player ban) ──────────────────────────

        [TestMethod]
        public async Task BaitAndSwitch_ForcesEraBanOntoNextPlayer()
        {
            // Submitter holds Bait & Switch and plays the banned-'a' word "cat" → the next player is
            // cursed with a personal 'a' ban for their upcoming turn.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var next = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "bait-and-switch");

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(next, state.TurnManager.CurrentPlayer, "Turn advanced to the next player.");
            Assert.AreEqual('a', state.GamePlayers[next].PersonalBannedLetter, "Next player is cursed with the offending 'a'.");
        }

        // ── Roulette Wheel (reward + self-tax) ──────────────────────────────

        [TestMethod]
        public async Task RouletteWheel_MultipliesCleanWord()
        {
            // Era ban 'z' (absent), rolled personal ban 'q' (absent from "cat") → clean → ×1.75 on
            // length 3 = 5.25 → 5 (half-up).
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;

            GiveModifier(state, submitter, "roulette-wheel");
            SetCardBan(state, submitter, "roulette-wheel", 'q');

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(5, state.GamePlayers[submitter].Score);
        }

        [TestMethod]
        public async Task RouletteWheel_TaxesWhenWordUsesRolledLetter()
        {
            // Rolled personal ban 't' is in "cat" → Zero-Point Tax → score 0 despite the ×1.75.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;

            GiveModifier(state, submitter, "roulette-wheel");
            SetCardBan(state, submitter, "roulette-wheel", 't');

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.AcceptedZeroPointTax>(result);
            Assert.AreEqual(0, state.GamePlayers[submitter].Score);
        }
    }
}
