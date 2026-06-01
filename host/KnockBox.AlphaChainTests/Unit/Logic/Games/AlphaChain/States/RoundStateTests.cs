using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    [TestClass]
    public class RoundStateTests
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

        /// <summary>
        /// Starts a 2–N player game (host as display) with a stubbed dictionary and an
        /// optional forced banned letter. Returns the engine + state so tests can drive
        /// submissions and ticks through the real command path.
        /// </summary>
        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartGameAsync(
            StubWordListService words, int playerCount = 2, bool survival = false, char? banned = null)
        {
            var engine = new AlphaChainGameEngine(
                words, new FixedRandomNumberService(), new ScoreCalculator(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            if (survival)
                state.UpdateSettings(s => s with { SurvivalMode = true });

            await engine.StartAsync(_host, state);

            if (banned is { } b)
                state.Execute(() => state.BannedLetter = b);

            return (engine, state);
        }

        private static async Task<SubmitWordResult> SubmitAsync(
            AlphaChainGameEngine engine, AlphaChainGameState state, string actor, string word)
        {
            var result = await engine.SubmitWordAsync(actor, word, state);
            Assert.IsTrue(result.TryGetSuccess(out var outcome), "SubmitWordAsync unexpectedly failed.");
            return outcome;
        }

        // ── Rejections ────────────────────────────────────────────────────────

        [TestMethod]
        public async Task Submit_NotActivePlayer_ReturnsRejectedNotYourTurn()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var notCurrent = state.TurnManager.TurnOrder[1];

            var outcome = await SubmitAsync(engine, state, notCurrent, "cat");

            Assert.IsInstanceOfType<SubmitWordResult.RejectedNotYourTurn>(outcome);
        }

        [TestMethod]
        public async Task Submit_EmptyWord_ReturnsRejectedEmpty()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            var outcome = await SubmitAsync(engine, state, current, "   ");

            Assert.IsInstanceOfType<SubmitWordResult.RejectedEmpty>(outcome);
        }

        [TestMethod]
        public async Task Submit_NotInDictionary_ReturnsRejectedNotInDictionary()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            var outcome = await SubmitAsync(engine, state, current, "zzz");

            Assert.IsInstanceOfType<SubmitWordResult.RejectedNotInDictionary>(outcome);
        }

        [TestMethod]
        public async Task Submit_BrokenChain_ReturnsRejectedChainBroken()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat", "dog"), banned: 'z');
            using var _ = state;

            // First word establishes RequiredStartLetter = 't'.
            var first = state.TurnManager.CurrentPlayer!;
            await SubmitAsync(engine, state, first, "cat");

            var second = state.TurnManager.CurrentPlayer!;
            var outcome = await SubmitAsync(engine, state, second, "dog"); // 'd' != 't'

            var broken = (SubmitWordResult.RejectedChainBroken)outcome;
            Assert.AreEqual('t', broken.Required);
        }

        [TestMethod]
        public async Task Submit_Duplicate_IsCaseInsensitive()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;

            var first = state.TurnManager.CurrentPlayer!;
            await SubmitAsync(engine, state, first, "cat");

            // Free the chain so the duplicate check (not the chain rule) is what fires.
            state.Execute(() => state.RequiredStartLetter = null);

            var second = state.TurnManager.CurrentPlayer!;
            var outcome = await SubmitAsync(engine, state, second, "CAT");

            Assert.IsInstanceOfType<SubmitWordResult.RejectedDuplicate>(outcome);
        }

        // ── Acceptance & scoring ────────────────────────────────────────────────

        [TestMethod]
        public async Task Submit_ValidWord_ReturnsAcceptedWithLengthScore()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            var outcome = await SubmitAsync(engine, state, current, "cat");

            var accepted = (SubmitWordResult.Accepted)outcome;
            Assert.AreEqual(3, accepted.Score);
            Assert.AreEqual(3, state.GamePlayers[current].Score);
            Assert.AreEqual('t', state.RequiredStartLetter);
        }

        [TestMethod]
        public async Task Submit_BannedLetterInsideWord_AppliesZeroPointTaxAndContinuesChain()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'a');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            var outcome = await SubmitAsync(engine, state, current, "cat");

            Assert.IsInstanceOfType<SubmitWordResult.AcceptedZeroPointTax>(outcome);
            Assert.AreEqual(0, state.GamePlayers[current].Score);
            // Banned 'a' is not the last letter, so the chain continues on 't'.
            Assert.AreEqual('t', state.RequiredStartLetter);
        }

        [TestMethod]
        public async Task Submit_BannedLetterAsLastLetter_ClearsRequiredStartLetter()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 't');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            var outcome = await SubmitAsync(engine, state, current, "cat");

            Assert.IsInstanceOfType<SubmitWordResult.AcceptedZeroPointTax>(outcome);
            Assert.IsNull(state.RequiredStartLetter);
        }

        // ── Shot-clock timeout ──────────────────────────────────────────────────

        [TestMethod]
        public async Task Timeout_SurvivalMode_EliminatesCurrentPlayer()
        {
            var (engine, state) = await StartGameAsync(
                new StubWordListService("cat"), playerCount: 3, survival: true, banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            engine.Tick(state.Context!, DateTimeOffset.UtcNow.AddMinutes(1));

            Assert.IsTrue(state.GamePlayers[current].IsEliminated);
            Assert.AreNotEqual(current, state.TurnManager.CurrentPlayer);
            Assert.AreNotEqual(AlphaChainGamePhase.GameOver, state.Phase);
        }

        [TestMethod]
        public async Task Timeout_NonSurvivalMode_KeepsPlayerAndScoresZero()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            engine.Tick(state.Context!, DateTimeOffset.UtcNow.AddMinutes(1));

            Assert.IsFalse(state.GamePlayers[current].IsEliminated);
            Assert.AreEqual(0, state.GamePlayers[current].Score);
            Assert.AreEqual(1, state.GamePlayers[current].TurnTimeouts);
            Assert.AreNotEqual(current, state.TurnManager.CurrentPlayer);
        }

        [TestMethod]
        public async Task Timeout_SurvivalMode_EndsGameWhenOneActivePlayerRemains()
        {
            var (engine, state) = await StartGameAsync(
                new StubWordListService("cat"), playerCount: 2, survival: true, banned: 'z');
            using var _ = state;

            // Two players → eliminating the active one leaves a single active player.
            engine.Tick(state.Context!, DateTimeOffset.UtcNow.AddMinutes(1));

            Assert.AreEqual(AlphaChainGamePhase.GameOver, state.Phase);
            Assert.IsNotNull(state.Results);
        }

        [TestMethod]
        public async Task Timeout_BeforeExpiry_IsNoOp()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            // now < PhaseEndTime → nothing happens.
            engine.Tick(state.Context!, DateTimeOffset.UtcNow);

            Assert.AreEqual(current, state.TurnManager.CurrentPlayer);
            Assert.AreEqual(0, state.GamePlayers[current].TurnTimeouts);
        }
    }
}
