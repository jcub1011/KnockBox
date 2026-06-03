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
    /// Exercises the 0-point utility cards whose effect is a submit-path rule rather than a score:
    /// The Wildcard (Succession bypass) and The Prism (clock refill on a failed/typo submission,
    /// once per turn), plus that an owner's own card-ban still taxes their word.
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
                words, new FixedRandomNumberService(), new EngineEvaluator(), new ModifierCardFactory(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

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

        private static void SetCardBan(AlphaChainGameState state, string playerId, string cardId, char letter) =>
            state.Execute(() => RoomStateProbe.SetCardBan(state, playerId, TestModifierCards.ToId(cardId), letter));

        // ── Own card-bans tax the owner's word ───────────────────────────────

        [TestMethod]
        public async Task OwnCardBan_TaxesTheOwnersWordToZero()
        {
            // Roulette Wheel rolls a personal 't' ban; "cat" uses it → the card-ban 't' taxes the
            // word to 0 despite the Roulette ×1.75.
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
            Assert.IsTrue(RoomStateProbe.PrismUsedThisTurn(state, submitter));

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
            // "yew": normally vowels=1 (e), consonants=2 (y, w) → Vowel Surge (×3 when vowels >
            // consonants) does NOT trigger. The Catalyst is placed BEFORE Vowel Surge so its vowel
            // override applies to it (the capability-walk idiom is position-dependent): y and w then
            // count as vowels too, so vowels=3 > consonants=2 and Vowel Surge fires: length 3 → ×3 = 9.
            var (engine, state) = await StartGameAsync(new StubWordListService("yew"), banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;

            GiveModifier(state, submitter, "catalyst");
            GiveModifier(state, submitter, "vowel-surge");

            await engine.SubmitWordAsync(submitter, "yew", state);

            Assert.AreEqual(9, state.GamePlayers[submitter].Score,
                "Catalyst (placed first) makes y/w vowels too → Vowel Surge triggers ×3 on the length-3 word.");
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
