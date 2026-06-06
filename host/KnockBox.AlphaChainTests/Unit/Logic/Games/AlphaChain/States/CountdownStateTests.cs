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
    /// Covers the pre-round "Get Ready" countdown: it arms its dwell from the host setting, holds
    /// until the dwell elapses, then opens the round — and ignores every command (it is not skippable).
    /// </summary>
    [TestClass]
    public class CountdownStateTests
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

        /// <summary>Starts a tutorials-off game so the FSM opens directly on the pre-round countdown,
        /// with the countdown length pinned to <paramref name="countdownSeconds"/>.</summary>
        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartAtCountdownAsync(
            int countdownSeconds = 5, int playerCount = 3)
        {
            var engine = new AlphaChainGameEngine(
                new StubWordListService(), new FixedRandomNumberService(), new EngineEvaluator(), new ModifierCardFactory(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            state.UpdateSettings(s => s with { EnableTutorials = false, PreRoundCountdownSeconds = countdownSeconds });
            await engine.StartAsync(_host, state);
            return (engine, state);
        }

        [TestMethod]
        public async Task OnEnter_SetsCountdownPhase_AndArmsDwellFromSetting()
        {
            var (_, state) = await StartAtCountdownAsync(countdownSeconds: 7);
            using var _ = state;

            Assert.AreEqual(AlphaChainGamePhase.Countdown, state.Phase);
            // The dwell is armed from the setting (real wall clock); allow generous slack for execution.
            var remaining = state.SubPhaseEndTime - DateTimeOffset.UtcNow;
            Assert.IsTrue(remaining > TimeSpan.Zero, "the countdown should still be running");
            Assert.IsTrue(remaining <= TimeSpan.FromSeconds(7), "the dwell must not exceed the configured length");
        }

        [TestMethod]
        public async Task Tick_BeforeDwellElapses_StaysInCountdown()
        {
            var (engine, state) = await StartAtCountdownAsync(countdownSeconds: 5);
            using var _ = state;

            engine.Tick(state.Context!, state.SubPhaseEndTime.AddSeconds(-1));

            Assert.AreEqual(AlphaChainGamePhase.Countdown, state.Phase);
        }

        [TestMethod]
        public async Task Tick_AfterDwellElapses_EntersRound()
        {
            var (engine, state) = await StartAtCountdownAsync(countdownSeconds: 5);
            using var _ = state;

            engine.Tick(state.Context!, state.SubPhaseEndTime.AddSeconds(1));

            Assert.AreEqual(AlphaChainGamePhase.Round, state.Phase);
            Assert.AreEqual(1, state.CurrentEra);
            Assert.AreEqual(1, state.CurrentRound);
        }

        [TestMethod]
        public async Task SkipCommand_IsIgnored_CountdownIsNotSkippable()
        {
            var (engine, state) = await StartAtCountdownAsync(countdownSeconds: 5);
            using var _ = state;

            // The host can skip tutorials, but not the Get Ready countdown — it always runs its dwell.
            await engine.SkipTutorialAsync(_host.Id, state);

            Assert.AreEqual(AlphaChainGamePhase.Countdown, state.Phase);
        }

        [TestMethod]
        public async Task SubmitWord_DuringCountdown_IsRejected_AndStaysInCountdown()
        {
            var (engine, state) = await StartAtCountdownAsync(countdownSeconds: 5);
            using var _ = state;
            var actor = state.TurnManager.CurrentPlayer!.Value;

            var outcome = await engine.SubmitWordAsync(actor, "cat", state);

            Assert.IsTrue(outcome.IsFailure, "no word may be submitted before the round opens");
            Assert.AreEqual(AlphaChainGamePhase.Countdown, state.Phase);
        }
    }
}
