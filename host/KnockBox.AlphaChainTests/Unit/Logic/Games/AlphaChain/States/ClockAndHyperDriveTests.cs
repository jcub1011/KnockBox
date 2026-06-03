using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
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
    /// Exercises the glass-cannon clock cards (Vault, Redline, Panic Button) via
    /// <see cref="AlphaChainGameState.ComputeArmedShotClockSeconds"/>, and the Hyper-Drive
    /// era-scoped latch via the real submit path with a deterministic submission timestamp.
    /// </summary>
    [TestClass]
    public class ClockAndHyperDriveTests
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

            // Pin the shot clock to 12s so the clock-math expectations below are independent of the
            // configured default (which is 20s).
            state.UpdateSettings(s => s with { EnableTutorials = false, ShotClockSeconds = 12 });
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

        private static void GiveModifier(AlphaChainGameState state, Guid playerId, string cardId) =>
            state.Execute(() => state.GamePlayers[playerId].EngineBay.Add(TestModifierCards.Create(cardId)));

        // ── Clock effects (ComputeArmedShotClockSeconds) ────────────────────

        [TestMethod]
        public async Task Vault_ShortensClockByTenPercent()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"));
            using var _ = state;
            var id = state.TurnManager.CurrentPlayer!.Value;
            GiveModifier(state, id, "the-vault");

            // 12 × 0.9 = 10.8 → 11 (half-up).
            Assert.AreEqual(11, state.ComputeArmedShotClockSeconds(state.GamePlayers[id]));
        }

        [TestMethod]
        public async Task Redline_ShortensClockByTwentyPercent()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"));
            using var _ = state;
            var id = state.TurnManager.CurrentPlayer!.Value;
            GiveModifier(state, id, "redline");

            // 12 × 0.8 = 9.6 → 10 (half-up).
            Assert.AreEqual(10, state.ComputeArmedShotClockSeconds(state.GamePlayers[id]));
        }

        [TestMethod]
        public async Task PanicButton_HalvesClock()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"));
            using var _ = state;
            var id = state.TurnManager.CurrentPlayer!.Value;
            GiveModifier(state, id, "panic-button");

            // 12 × 0.5 = 6.
            Assert.AreEqual(6, state.ComputeArmedShotClockSeconds(state.GamePlayers[id]));
        }

        [TestMethod]
        public async Task ClockEffects_Stack_FractionThenSeconds()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"));
            using var _ = state;
            var id = state.TurnManager.CurrentPlayer!.Value;
            GiveModifier(state, id, "redline");    // −20%
            GiveModifier(state, id, "the-vault");  // −10%

            // Fractions sum: −20% − 10% = −30% → 12 × 0.7 = 8.4 → 8 (half-up).
            Assert.AreEqual(8, state.ComputeArmedShotClockSeconds(state.GamePlayers[id]));
        }

        [TestMethod]
        public async Task ClockEffects_FlooredAtMinimum()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"));
            using var _ = state;
            var id = state.TurnManager.CurrentPlayer!.Value;
            GiveModifier(state, id, "panic-button"); // −50%
            GiveModifier(state, id, "redline");      // −20%
            GiveModifier(state, id, "the-vault");    // −10%

            // −50% − 20% − 10% = −80% → 12 × 0.2 = 2.4 → 2 → floored to MinShotClockSeconds (3).
            Assert.AreEqual(AlphaChainGameState.MinShotClockSeconds,
                state.ComputeArmedShotClockSeconds(state.GamePlayers[id]));
        }

        [TestMethod]
        public async Task HeatSink_LengthensTheShotClock()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"));
            using var _ = state;
            var id = state.TurnManager.CurrentPlayer!.Value;
            GiveModifier(state, id, "heat-sink");

            // Heat Sink declares a +0.3 fractional clock effect, so 12 × (1 + 0.3) = 15.6 → 16.
            Assert.AreEqual(16, state.ComputeArmedShotClockSeconds(state.GamePlayers[id]));
        }

        [TestMethod]
        public async Task AnchorChain_PinsClockToFive_IgnoringEveryOtherClockEffect()
        {
            var (_, state) = await StartGameAsync(new StubWordListService("cat"));
            using var _ = state;
            var id = state.TurnManager.CurrentPlayer!.Value;
            GiveModifier(state, id, "anchor-chain"); // fixed, unmodifiable 5s
            GiveModifier(state, id, "heat-sink");    // +5s … which the override ignores
            GiveModifier(state, id, "the-vault");    // −3s … also ignored

            Assert.AreEqual(5, state.ComputeArmedShotClockSeconds(state.GamePlayers[id]),
                "The Anchor Chain pins the clock to a strict, unmodifiable 5 seconds.");
        }

        // ── Hyper-Drive latch (submit path) ─────────────────────────────────

        [TestMethod]
        public async Task HyperDrive_LatchesWhenSubmittingFast()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var id = state.TurnManager.CurrentPlayer!.Value;
            GiveModifier(state, id, "hyper-drive");

            // Arm the clock to a known window, then submit 2s in (elapsed 2 < 3 threshold).
            var armAt = DateTimeOffset.UtcNow;
            state.Execute(() => state.PhaseEndTime = armAt.AddSeconds(12));
            await engine.SubmitWordAsync(id, "cat", state, armAt.AddSeconds(2));

            Assert.IsTrue(RoomStateProbe.HyperDriveActive(state, id), "Fast submit should latch Hyper-Drive.");
            // Once latched, the owner's clock is overridden to the rule's 5s.
            Assert.AreEqual(5, state.ComputeArmedShotClockSeconds(state.GamePlayers[id]));
        }

        [TestMethod]
        public async Task HyperDrive_DoesNotLatchWhenSubmittingSlow()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var id = state.TurnManager.CurrentPlayer!.Value;
            GiveModifier(state, id, "hyper-drive");

            // Submit 10s in (elapsed 10 ≥ 3 threshold) → no latch.
            var armAt = DateTimeOffset.UtcNow;
            state.Execute(() => state.PhaseEndTime = armAt.AddSeconds(12));
            await engine.SubmitWordAsync(id, "cat", state, armAt.AddSeconds(10));

            Assert.IsFalse(RoomStateProbe.HyperDriveActive(state, id), "Slow submit must not latch Hyper-Drive.");
        }
    }
}
