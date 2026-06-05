using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    /// <summary>
    /// Submit-path coverage for the new cards whose behavior lives outside the pure scoring fold:
    /// Slow Burn's length-floor Zero-Point Tax, Tax Write-Off's first-letter salvage, and Chrono
    /// Syphon's per-second reactive bounty.
    /// </summary>
    [TestClass]
    public class NewEconomyCardTests
    {
        private Mock<ILogger<AlphaChainGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<AlphaChainGameState>> _stateLoggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<AlphaChainGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<AlphaChainGameState>>();
            _host = UserFactory.Create("Host", Guid.NewGuid());
        }

        private static User MakePlayer(int index) => UserFactory.Create($"Player{index}", Guid.NewGuid());

        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartGameAsync(
            StubWordListService words, int playerCount = 2, char? banned = null)
        {
            var engine = new AlphaChainGameEngine(
                words, new FixedRandomNumberService(), new EngineEvaluator(), new ModifierCardFactory(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            state.UpdateSettings(s => s with { EnableTutorials = false, ShotClockSeconds = 12 });
            await engine.StartAsync(_host, state);
            DrainCountdown(engine, state);

            if (banned is { } b)
                state.Execute(() => state.BannedLetter = b);

            return (engine, state);
        }

        private static void DrainCountdown(AlphaChainGameEngine engine, AlphaChainGameState state)
        {
            if (state.Phase == AlphaChainGamePhase.Countdown)
                engine.Tick(state.Context!, state.SubPhaseEndTime.AddSeconds(1));
        }

        private static void GiveModifier(AlphaChainGameState state, Guid playerId, string cardId) =>
            state.Execute(() => state.GamePlayers[playerId].EngineBay.Add(TestModifierCards.Create(cardId)));

        /// <summary>Score the player clear of last place so the era ban actually taxes them (the era ban
        /// never taxes the last-place player, which on a fresh 0–0 field is the tie-broken turn-0 seat).</summary>
        private const int NotLastPlaceScore = 100;
        private static void ParkClearOfLastPlace(AlphaChainGameState state, Guid playerId) =>
            state.Execute(() => state.GamePlayers[playerId].Score = NotLastPlaceScore);

        // ── Slow Burn — length floor routed through the Zero-Point Tax ──────

        [TestMethod]
        public async Task SlowBurn_ShortWord_IsTaxed_AndSiphonable()
        {
            // Banned 'z' is absent from "cat", so only Slow Burn's 6-letter floor makes it illegal.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!.Value;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "slow-burn");
            GiveModifier(state, owner, TestModifierCards.TaxCollectorId);

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.AcceptedZeroPointTax>(result);

            Assert.AreEqual(0, state.GamePlayers[submitter].Score, "Too-short word is taxed to 0.");
            // Would-be score is the bare length 3 → opponent Tax Collector takes round(3 × 0.5) = 2.
            Assert.AreEqual(2, state.GamePlayers[owner].Score, "Slow Burn tax is siphonable like a ban.");
        }

        [TestMethod]
        public async Task SlowBurn_LongEnoughWord_ScoresNormally()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("bridge"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!.Value;

            GiveModifier(state, submitter, "slow-burn");

            var outcome = await engine.SubmitWordAsync(submitter, "bridge", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result);

            // 6 letters ≥ floor → legal → bare length 6 (Slow Burn is inert in scoring).
            Assert.AreEqual(6, state.GamePlayers[submitter].Score);
        }

        // ── Tax Write-Off — salvage the first letter on a self-taxed word ───

        [TestMethod]
        public async Task TaxWriteOff_AddsFirstLetterScore_OnTopOfTaxedWord()
        {
            // Banned 'a' taxes "cat". Bay [tax-write-off, anchor]: the salvage re-scores "c" through the
            // bay → seed 1 + anchor 10 = 11, added on top of the taxed 0.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!.Value;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "tax-write-off");
            GiveModifier(state, submitter, "anchor");
            GiveModifier(state, owner, TestModifierCards.TaxCollectorId);
            ParkClearOfLastPlace(state, submitter);

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(NotLastPlaceScore + 11, state.GamePlayers[submitter].Score, "First letter scored clean (1 + 10) on top of the baseline.");
            // The original word stays taxed and siphonable: would-be 13 → opponent takes round(13 × 0.5) = 7.
            Assert.AreEqual(7, state.GamePlayers[owner].Score, "Original taxed word is still siphonable.");
        }

        [TestMethod]
        public async Task IrsAgentAndTaxWriteOff_Stack_SuppressBountyAndSalvageFirstLetter()
        {
            // Pins the interaction of the two tax rules that resolve in sequence (IRS override first,
            // then Tax Write-Off salvage on top). Banned 'a' taxes "cat"; bay [irs, tax-write-off, anchor]:
            // IRS forces the owner's taxed score to 0 and suppresses the bounty, then the write-off
            // re-scores "c" clean (seed 1 + anchor 10 = 11) and adds it on top.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!.Value;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "irs");
            GiveModifier(state, submitter, "tax-write-off");
            GiveModifier(state, submitter, "anchor");
            GiveModifier(state, owner, TestModifierCards.TaxCollectorId);
            ParkClearOfLastPlace(state, submitter);

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(NotLastPlaceScore + 11, state.GamePlayers[submitter].Score, "IRS grants 0, write-off salvages 11 on top of the baseline.");
            Assert.AreEqual(0, state.GamePlayers[owner].Score, "IRS suppresses the Tax Collector bounty.");
        }

        // ── Chrono Syphon — a point per second left on an opponent's clock ──

        [TestMethod]
        public async Task ChronoSyphon_AwardsOwnerOnePerSecondLeftInOpponentSubmission()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!.Value;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, owner, "chrono-syphon");

            // Arm the submitter's clock to a 12s window and submit with 8s left.
            var armAt = DateTimeOffset.UtcNow;
            state.Execute(() => state.PhaseEndTime = armAt.AddSeconds(12));
            await engine.SubmitWordAsync(submitter, "cat", state, armAt.AddSeconds(4));

            Assert.AreEqual(8, state.GamePlayers[owner].Score, "Owner banks +1 per second remaining (8).");
        }

        [TestMethod]
        public async Task ChronoSyphon_DoesNotCreditTheSubmitterThemselves()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!.Value;

            GiveModifier(state, submitter, "chrono-syphon");

            var armAt = DateTimeOffset.UtcNow;
            state.Execute(() => state.PhaseEndTime = armAt.AddSeconds(12));
            await engine.SubmitWordAsync(submitter, "cat", state, armAt.AddSeconds(4));

            // Chrono Syphon only pays its owner on OTHER players' submissions, so the holder earns only
            // the clean word's base score (3), not a self-syphon.
            Assert.AreEqual(3, state.GamePlayers[submitter].Score);
        }
    }
}
