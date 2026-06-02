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
    /// Exercises the 0-point utility cards whose effect is a submit-path rule rather than a score:
    /// The Faraday Cage (immunity to the owner's own card-bans), The Wildcard (Succession bypass),
    /// and The Prism (clock refill on a failed/typo submission, once per turn).
    /// </summary>
    [TestClass]
    public class UtilityCardTests
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

            state.UpdateSettings(s => s with { EnableTutorials = false });
            await engine.StartAsync(_host, state);

            if (banned is { } b)
                state.Execute(() => state.BannedLetter = b);

            return (engine, state);
        }

        private static void GiveModifier(AlphaChainGameState state, string playerId, string cardId) =>
            state.Execute(() => state.GamePlayers[playerId].EngineBay.Add(ModifierLibrary.FindById(cardId)!));

        private static void SetCardBan(AlphaChainGameState state, string playerId, string cardId, char letter) =>
            state.Execute(() => state.GamePlayers[playerId].CardBannedLetters[cardId] = letter);

        // ── The Faraday Cage (immune to own card-bans) ───────────────────────

        [TestMethod]
        public async Task FaradayCage_MakesOwnerImmuneToTheirOwnCardBan()
        {
            // Roulette Wheel rolls a personal 't' ban; "cat" uses it. The Faraday Cage makes the
            // owner immune, so the word is NOT taxed and still earns the Roulette ×1.75 (3 → 5).
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;

            GiveModifier(state, submitter, "roulette-wheel");
            GiveModifier(state, submitter, "faraday-cage");
            SetCardBan(state, submitter, "roulette-wheel", 't');

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result, "Faraday Cage immunity → not taxed.");
            Assert.AreEqual(5, state.GamePlayers[submitter].Score, "Roulette ×1.75 on the clean (immune) word.");
        }

        [TestMethod]
        public async Task WithoutFaradayCage_OwnCardBanStillTaxes()
        {
            // Control: same setup minus the Faraday Cage → the card-ban 't' taxes the word to 0.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;

            GiveModifier(state, submitter, "roulette-wheel");
            SetCardBan(state, submitter, "roulette-wheel", 't');

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.AcceptedZeroPointTax>(result);
            Assert.AreEqual(0, state.GamePlayers[submitter].Score);
        }

        // ── The Wildcard (Succession bypass) ─────────────────────────────────

        [TestMethod]
        public async Task Wildcard_IgnoresTheSuccessionRule()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            state.Execute(() => state.RequiredStartLetter = 'q'); // "cat" would normally break the chain

            GiveModifier(state, submitter, "wildcard");

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result, "The Wildcard bypasses the required start letter.");
        }

        [TestMethod]
        public async Task WithoutWildcard_SuccessionRuleIsEnforced()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            state.Execute(() => state.RequiredStartLetter = 'q');

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.RejectedChainBroken>(result);
        }

        // ── The Prism (clock refill on a failed submission, once per turn) ────

        [TestMethod]
        public async Task Prism_RefillsClockOnInvalidWord_OncePerTurn()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            GiveModifier(state, submitter, "prism");

            // Clock about to expire; an invalid word would normally just let it run down.
            var t0 = DateTimeOffset.UtcNow;
            state.Execute(() => state.PhaseEndTime = t0.AddSeconds(2));

            var first = await engine.SubmitWordAsync(submitter, "zzz", state, t0); // not in dictionary
            Assert.IsTrue(first.TryGetSuccess(out var r1));
            Assert.IsInstanceOfType<SubmitWordResult.RejectedNotInDictionary>(r1);
            Assert.AreEqual(t0.AddSeconds(20), state.PhaseEndTime, "The Prism refills the clock to a full 20s (the default).");
            Assert.IsTrue(state.GamePlayers[submitter].PrismUsedThisTurn);

            // A second typo the SAME turn must not refill again (once per turn).
            state.Execute(() => state.PhaseEndTime = t0.AddSeconds(2));
            var second = await engine.SubmitWordAsync(submitter, "qqq", state, t0.AddSeconds(1));
            Assert.IsTrue(second.TryGetSuccess(out var r2));
            Assert.IsInstanceOfType<SubmitWordResult.RejectedNotInDictionary>(r2);
            Assert.AreEqual(t0.AddSeconds(2), state.PhaseEndTime, "No second refill this turn.");
        }

        // ── The Catalyst (Y/W/H count as both vowel and consonant) ───────────

        [TestMethod]
        public async Task Catalyst_FlipsAVowelConditional_ThroughTheSubmitPath()
        {
            // "yew": normally vowels=1 (e), consonants=2 (y, w) → Vowel Surge (×2 when vowels >
            // consonants) does NOT trigger. With The Catalyst, y and w count as both, so vowels=3 >
            // consonants=2 and Vowel Surge fires: length 3 → ×2 = 6.
            var (engine, state) = await StartGameAsync(new StubWordListService("yew"), banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;

            GiveModifier(state, submitter, "vowel-surge");
            GiveModifier(state, submitter, "catalyst");

            await engine.SubmitWordAsync(submitter, "yew", state);

            Assert.AreEqual(6, state.GamePlayers[submitter].Score,
                "Catalyst makes y/w vowels too → Vowel Surge triggers ×2 on the length-3 word.");
        }

        [TestMethod]
        public async Task WithoutCatalyst_VowelConditionalStaysFalse()
        {
            // Control: same word and Vowel Surge, no Catalyst → vowels (1) ≤ consonants (2), so the
            // multiplier does not fire and the word just scores its length.
            var (engine, state) = await StartGameAsync(new StubWordListService("yew"), banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;

            GiveModifier(state, submitter, "vowel-surge");

            await engine.SubmitWordAsync(submitter, "yew", state);

            Assert.AreEqual(3, state.GamePlayers[submitter].Score, "No Catalyst → Vowel Surge stays inert.");
        }
    }
}
