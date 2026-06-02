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
    /// Exercises the tax/ban economy cards through the real submit path: the Enforcer siphon and
    /// max-rate rule, Smuggler's Toll (card-ban siphon), IRS (own-tax salvage + bounty suppression),
    /// Bait &amp; Switch (forced next-player ban), and Roulette Wheel (reward + self-tax).
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
                words, new FixedRandomNumberService(), new ScoreCalculator(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            state.UpdateSettings(s => s with { EnableTutorials = false });
            await engine.StartAsync(_host, state);
            state.Execute(() => state.BannedLetter = banned);
            return (engine, state);
        }

        private static void GiveModifier(AlphaChainGameState state, string playerId, string cardId) =>
            state.Execute(() => state.GamePlayers[playerId].EngineBay.Add(ModifierLibrary.FindById(cardId)!));

        private static void SetCardBan(AlphaChainGameState state, string playerId, string cardId, char letter) =>
            state.Execute(() => state.GamePlayers[playerId].CardBannedLetters[cardId] = letter);

        // ── Enforcer / max-rate ─────────────────────────────────────────────

        [TestMethod]
        public async Task Enforcer_CollectsSeventyFivePercent()
        {
            // "cat" + Anchor → would-be 15; banned 'a' taxes it. Enforcer owner takes 75% → 11.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "anchor");
            GiveModifier(state, owner, "enforcer");

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(0, state.GamePlayers[submitter].Score);
            Assert.AreEqual(11, state.GamePlayers[owner].Score, "Enforcer collects round(15 × 0.75).");
        }

        [TestMethod]
        public async Task TaxCollectorPlusEnforcer_TakesHighestRate_NotSum()
        {
            // Holding both must not stack (0.5 + 0.75); the max rate (0.75 → 11) wins.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "anchor");
            GiveModifier(state, owner, ModifierLibrary.TaxCollectorId);
            GiveModifier(state, owner, "enforcer");

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(11, state.GamePlayers[owner].Score, "Max rate 0.75, not summed 1.25.");
        }

        // ── Smuggler's Toll (card-ban siphon) ───────────────────────────────

        [TestMethod]
        public async Task SmugglersToll_MintsCut_WhenOpponentUsesRolledLetter()
        {
            // Clean word (banned 'z' absent from "cat"); submitter Anchor → earns 15. Owner's
            // Smuggler letter 't' is in "cat" → owner minted round(15 × 0.2) = 3; submitter keeps 15.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "anchor");
            GiveModifier(state, owner, "smugglers-toll");
            SetCardBan(state, owner, "smugglers-toll", 't');

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(15, state.GamePlayers[submitter].Score, "Submitter keeps their full score.");
            Assert.AreEqual(3, state.GamePlayers[owner].Score, "Owner is minted 20% of the earned 15.");
        }

        [TestMethod]
        public async Task SmugglersToll_PaysNothing_OnTaxedWord()
        {
            // Banned 'a' taxes "cat" to 0 → no earned points → no Smuggler mint even though 't' is used.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, owner, "smugglers-toll");
            SetCardBan(state, owner, "smugglers-toll", 't');

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(0, state.GamePlayers[owner].Score);
        }

        // ── IRS (own-tax salvage) ───────────────────────────────────────────

        [TestMethod]
        public async Task Irs_SalvagesFlatFifteen_AndSuppressesBounty()
        {
            // Submitter holds only IRS (would-be = length 3); banned 'a' taxes "cat". IRS salvages a
            // flat 15 and denies the opposing Tax Collector its cut.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "irs");
            GiveModifier(state, owner, ModifierLibrary.TaxCollectorId);

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result, "IRS salvage reads as a normal accept.");

            Assert.AreEqual(15, state.GamePlayers[submitter].Score, "Flat 15, not the would-be 3.");
            Assert.AreEqual(0, state.GamePlayers[owner].Score, "Tax Collector bounty is suppressed by IRS.");
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
            Assert.AreEqual('a', state.GamePlayers[next].PersonalBannedLetter);
            Assert.IsNull(state.PendingForcedPersonalBan, "The pending ban is consumed once applied.");
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
