using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
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
    /// Submit-path coverage for the Magnifying Glass reaching beyond the scoring fold: it magnifies the
    /// clock delta of the card to its right (<see cref="AlphaChainGameState.ComputeArmedShotClockSeconds"/>)
    /// and an opponent-reactive economy value (Tax Collector's siphon), with the immediate-right-neighbor
    /// rule honored in each context the cards run in.
    /// </summary>
    [TestClass]
    public class MagnifyingGlassCardTests
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
        private static void ParkClearOfLastPlace(AlphaChainGameState state, Guid playerId) =>
            state.Execute(() => state.GamePlayers[playerId].Score = 100);

        // ── Clock delta magnification ───────────────────────────────────────

        [TestMethod]
        public async Task Glass_MagnifiesTheClockDeltaOfTheCardToItsRight()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"));
            using var _ = state;
            var id = state.TurnManager.CurrentPlayer!.Value;
            var other = state.TurnManager.TurnOrder[1];

            // Baseline (self-contained): Redline alone arms a 12s clock to 10 (−20% → 12 × 0.8 = 9.6 → 10).
            GiveModifier(state, other, "redline");
            Assert.AreEqual(10, state.ComputeArmedShotClockSeconds(state.GamePlayers[other]),
                "Redline alone: −20% off a 12s clock → 10.");

            GiveModifier(state, id, "magnifying-glass"); // index 0
            GiveModifier(state, id, "redline");          // index 1 — immediately to the glass's right

            // Redline's −20% behind the glass becomes −30% → 12 × 0.7 = 8.4 → 8 (half-up).
            Assert.AreEqual(8, state.ComputeArmedShotClockSeconds(state.GamePlayers[id]));
        }

        // ── Economy / reactive value magnification ──────────────────────────

        [TestMethod]
        public async Task Glass_MagnifiesAnOpponentTaxCollectorSiphon()
        {
            // Banned 'a' taxes "cat". The submitter's Anchor makes the would-be score 3 + 10 = 13.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!.Value;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "anchor");
            GiveModifier(state, owner, "magnifying-glass");              // owner bay index 0
            GiveModifier(state, owner, TestModifierCards.TaxCollectorId); // index 1 — magnified ×1.5
            ParkClearOfLastPlace(state, submitter); // so "cat" is era-taxed → the siphon fires

            await engine.SubmitWordAsync(submitter, "cat", state);

            // Tax Collector normally takes round(13 × 0.5) = 7; behind the glass round(13 × 0.5 × 1.5) = 10.
            Assert.AreEqual(10, state.GamePlayers[owner].Score);
        }
    }
}
