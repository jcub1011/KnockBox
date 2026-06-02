using System.Text;
using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.FSM.States;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using KnockBox.WordService.Contracts;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    /// <summary>
    /// Covers the tutorial flow: the Shiritori tutorial at game start (and the ban-free first era),
    /// the Engine tutorial at the first era boundary, host skip vs auto-advance, the host-only skip
    /// guard, and the once-per-match guarantee across a full match.
    /// </summary>
    [TestClass]
    public class TutorialStateTests
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
            IWordListService words, int playerCount, Action<AlphaChainGameState>? configure = null)
        {
            var engine = new AlphaChainGameEngine(
                words, new FixedRandomNumberService(), new ScoreCalculator(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            configure?.Invoke(state);
            await engine.StartAsync(_host, state);
            return (engine, state);
        }

        // ── Game start (Shiritori) + ban-free era 1 ─────────────────────────────

        [TestMethod]
        public async Task Start_WithTutorials_EntersShiritori_AndEraOneIsBanFree()
        {
            var (_, state) = await StartGameAsync(new StubWordListService(), playerCount: 3);
            using var _ = state;

            Assert.AreEqual(AlphaChainGamePhase.Tutorial, state.Phase);
            Assert.AreEqual(TutorialKind.Shiritori, state.CurrentTutorial);
            Assert.IsTrue(state.ShownTutorials.Contains(TutorialKind.Shiritori));
            Assert.IsNull(state.BannedLetter, "era 1 is ban-free");
        }

        [TestMethod]
        public async Task Start_WithoutTutorials_GoesStraightToRound_AndEraOneIsBanFree()
        {
            var (_, state) = await StartGameAsync(new StubWordListService(), playerCount: 3,
                configure: s => s.UpdateSettings(c => c with { EnableTutorials = false }));
            using var _ = state;

            Assert.AreEqual(AlphaChainGamePhase.Round, state.Phase);
            Assert.IsNull(state.BannedLetter, "era 1 is ban-free");
        }

        // ── Auto-advance + host skip ────────────────────────────────────────────

        [TestMethod]
        public async Task ShiritoriTutorial_TimesOut_EntersRound()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService(), playerCount: 3);
            using var _ = state;
            var t0 = DateTimeOffset.UtcNow;

            // A tick before the dwell elapses stays in the tutorial.
            engine.Tick(state.Context!, t0.AddSeconds(1));
            Assert.AreEqual(AlphaChainGamePhase.Tutorial, state.Phase);

            // Past the dwell → the round begins.
            engine.Tick(state.Context!, t0.AddSeconds(TutorialState.DurationFor(TutorialKind.Shiritori).TotalSeconds + 1));
            Assert.AreEqual(AlphaChainGamePhase.Round, state.Phase);
        }

        [TestMethod]
        public async Task ShiritoriTutorial_HostSkip_EntersRoundImmediately()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService(), playerCount: 3);
            using var _ = state;

            var result = await engine.SkipTutorialAsync(_host.Id, state);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(AlphaChainGamePhase.Round, state.Phase);
        }

        [TestMethod]
        public async Task Tutorial_NonHostSkip_Rejected_AndStays()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService(), playerCount: 3);
            using var _ = state;

            var result = await engine.SkipTutorialAsync(state.TurnManager.TurnOrder[0], state);

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(AlphaChainGamePhase.Tutorial, state.Phase);
        }

        // ── Engine tutorial at the first era boundary ───────────────────────────

        [TestMethod]
        public async Task FirstEraBoundary_WithTutorials_ShowsEngineThenIntermission()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat", "tea", "ant"), playerCount: 3,
                configure: s => s.UpdateSettings(c => c with { EraInterval = 1, EraCount = 3 }));
            using var _ = state;

            // Skip the opening Shiritori tutorial to begin round 1.
            await engine.SkipTutorialAsync(_host.Id, state);
            Assert.AreEqual(AlphaChainGamePhase.Round, state.Phase);

            // Round 1: the chain wraps → era 1 ends. With no cards yet there's no replay hold, so
            // the Engine tutorial opens immediately.
            await engine.SubmitWordAsync(state.TurnManager.TurnOrder[0], "cat", state);
            await engine.SubmitWordAsync(state.TurnManager.TurnOrder[1], "tea", state);
            DrainReplayHold(engine, state);
            await engine.SubmitWordAsync(state.TurnManager.TurnOrder[2], "ant", state);
            DrainReplayHold(engine, state);

            Assert.AreEqual(AlphaChainGamePhase.Tutorial, state.Phase);
            Assert.AreEqual(TutorialKind.Engine, state.CurrentTutorial);

            // Skip → the Intermission proper opens (cards dealt, Optimization).
            await engine.SkipTutorialAsync(_host.Id, state);
            Assert.AreEqual(AlphaChainGamePhase.Intermission, state.Phase);
            Assert.AreEqual(IntermissionSubPhase.Optimization, state.IntermissionPhase);
        }

        // ── Once-per-match across a full match ──────────────────────────────────

        [TestMethod]
        public async Task FullMatch_WithTutorials_ShowsEachTutorialExactlyOnce()
        {
            var (engine, state) = await StartGameAsync(new AnyWordListService(), playerCount: 3,
                configure: s => s.UpdateSettings(c => c with { EraInterval = 1, EraCount = 3 }));
            using var _ = state;

            var seen = new List<TutorialKind>();
            var clock = DateTimeOffset.UtcNow;
            int counter = 0;
            int guard = 0;

            while (state.Phase != AlphaChainGamePhase.GameOver && guard++ < 2000)
            {
                if (state.Phase == AlphaChainGamePhase.Tutorial)
                {
                    seen.Add(state.CurrentTutorial);
                    await engine.SkipTutorialAsync(_host.Id, state);
                }
                else if (state.Phase == AlphaChainGamePhase.Intermission
                         && state.IntermissionPhase == IntermissionSubPhase.TaxTutorial)
                {
                    seen.Add(TutorialKind.Tax);
                    await engine.SkipTutorialAsync(_host.Id, state);
                }
                else if (state.Phase == AlphaChainGamePhase.Intermission)
                {
                    clock = clock.AddSeconds(100);
                    engine.Tick(state.Context!, clock);
                }
                else if (state.PendingTransitionAt is { } holdUntil)
                {
                    engine.Tick(state.Context!, holdUntil.AddSeconds(1));
                }
                else
                {
                    var actor = state.TurnManager.CurrentPlayer!;
                    var word = NextWord(state.RequiredStartLetter, state.BannedLetter, ref counter);
                    var outcome = await engine.SubmitWordAsync(actor, word, state);
                    Assert.IsTrue(outcome.IsSuccess, $"submission '{word}' failed");
                }
            }

            Assert.AreEqual(AlphaChainGamePhase.GameOver, state.Phase);
            Assert.AreEqual(1, seen.Count(k => k == TutorialKind.Shiritori), "Shiritori shows once");
            Assert.AreEqual(1, seen.Count(k => k == TutorialKind.Engine), "Engine shows once (era 1 boundary)");
            Assert.AreEqual(1, seen.Count(k => k == TutorialKind.Tax), "Tax shows once (first Intermission)");
        }

        // Ticks past a pending end-of-round score-replay hold so the transition fires.
        private static void DrainReplayHold(AlphaChainGameEngine engine, AlphaChainGameState state)
        {
            if (state.PendingTransitionAt is { } holdUntil)
                engine.Tick(state.Context!, holdUntil.AddSeconds(1));
        }

        // Builds a unique chained word that avoids the active banned letter (mirrors the
        // full-game simulation's generator).
        private static string NextWord(char? requiredStart, char? banned, ref int counter)
        {
            char bannedChar = banned ?? '\0';
            var alphabet = new StringBuilder();
            for (char c = 'b'; c <= 'z'; c++)
                if (c != bannedChar) alphabet.Append(c);
            string alpha = alphabet.ToString();

            char endLetter = bannedChar == 'e' ? 'o' : 'e';
            char startLetter = requiredStart ?? (bannedChar == 'b' ? 'c' : 'b');

            int n = counter++;
            var mid = new StringBuilder();
            do
            {
                mid.Insert(0, alpha[n % alpha.Length]);
                n /= alpha.Length;
            } while (n > 0);

            return $"{startLetter}{mid}{endLetter}";
        }
    }
}
