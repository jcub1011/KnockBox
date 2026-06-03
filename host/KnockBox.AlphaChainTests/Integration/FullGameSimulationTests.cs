using System.Text;
using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Integration
{
    /// <summary>
    /// Drives complete Alpha Chain matches end-to-end through the real engine/FSM on a
    /// deterministic RNG and a controlled clock (intermissions are stepped via <c>Tick</c>;
    /// rounds never time out because every turn is answered immediately). Asserts the match
    /// reaches <c>GameOver</c> cleanly, the winner genuinely has the top score, every
    /// submission was dictionary-valid, and each Engine Bay stays within its invariants.
    /// </summary>
    [TestClass]
    public class FullGameSimulationTests
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

        [TestMethod]
        public async Task FourPlayers_TwoEras_CompletesWithTopScoringWinner()
            => await RunSimulationAsync(playerCount: 4, eraInterval: 2, eraCount: 2);

        [TestMethod]
        public async Task SixPlayers_FourEras_CompletesWithTopScoringWinner()
            => await RunSimulationAsync(playerCount: 6, eraInterval: 2, eraCount: 4);

        private async Task RunSimulationAsync(int playerCount, int eraInterval, int eraCount)
        {
            var words = new AnyWordListService();
            var engine = new AlphaChainGameEngine(
                words, new FixedRandomNumberService(), new EngineEvaluator(), new ModifierCardFactory(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            using var _ = state;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(UserFactory.Create($"Player{i}", $"p{i}-id"));

            // Tutorials off — this drives raw gameplay end-to-end; the tutorial phases are covered
            // by dedicated unit tests.
            state.UpdateSettings(s => s with { EraInterval = eraInterval, EraCount = eraCount, EnableTutorials = false });
            await engine.StartAsync(_host, state);

            // Era 1 is ban-free; from era 2 the SniperBan timeout draws a banned letter. Generated
            // words avoid the active banned letter (and every previous word), so the chain never
            // breaks and no Zero-Point Tax fires.
            int counter = 0;
            int submissions = 0;
            const int safetyCap = 5000;

            while (state.Phase != AlphaChainGamePhase.GameOver && submissions < safetyCap)
            {
                if (state.Phase == AlphaChainGamePhase.Intermission)
                {
                    StepIntermissionToCompletion(engine, state);
                    continue;
                }

                // A round-ending word leaves the FSM holding in RoundState so its score animation
                // can finish; tick past the hold to fire the transition (as the host tick does).
                if (state.PendingTransitionAt is { } holdUntil)
                {
                    engine.Tick(state.Context!, holdUntil.AddSeconds(1));
                    continue;
                }

                // RoundState: the active player answers immediately (no timeout).
                var actor = state.TurnManager.CurrentPlayer!;
                var word = NextWord(state.RequiredStartLetter, state.BannedLetter, ref counter);

                var outcome = await engine.SubmitWordAsync(actor, word, state);
                Assert.IsTrue(outcome.TryGetSuccess(out var result),
                    $"Submission of '{word}' failed at the engine layer.");

                // "All words validated against the dictionary": the chain never produced an
                // out-of-dictionary or otherwise rejected word.
                Assert.IsFalse(result is SubmitWordResult.RejectedNotInDictionary
                                      or SubmitWordResult.RejectedChainBroken
                                      or SubmitWordResult.RejectedDuplicate
                                      or SubmitWordResult.RejectedEmpty
                                      or SubmitWordResult.RejectedNotYourTurn,
                    $"Word '{word}' was rejected: {result.GetType().Name}.");

                submissions++;
            }

            // ── No infinite loop; the match terminated in GameOver with standings. ──
            Assert.IsTrue(submissions < safetyCap, "Simulation hit the safety cap — the game never ended.");
            Assert.AreEqual(AlphaChainGamePhase.GameOver, state.Phase);
            Assert.IsNotNull(state.Results);

            var results = state.Results!;
            Assert.AreEqual(playerCount, results.Rankings.Count, "Every player should appear in the standings.");
            Assert.AreEqual(state.PlayLog.Count, results.TotalWordsPlayed);

            // ── Winner genuinely holds the top score (non-survival mode). ──
            int topScore = state.GamePlayers.Values.Max(p => p.Score);
            var winner = state.GamePlayers[results.WinnerUserId];
            Assert.AreEqual(topScore, winner.Score, "The winner must have the highest score.");
            Assert.IsFalse(winner.IsEliminated, "A non-survival winner is never eliminated.");
            Assert.AreEqual(topScore, results.Rankings[0].Score, "Rank 1 must carry the top score.");

            // ── Cards consumed/managed correctly across the whole match. ──
            foreach (var player in state.GamePlayers.Values)
            {
                Assert.IsTrue(player.EngineBay.Count <= player.ModifierSlots,
                    $"Player {player.UserId} overflowed their Engine Bay ({player.EngineBay.Count} > {player.ModifierSlots}).");

                var distinctIds = player.EngineBay.Select(c => c.GetId()).Distinct().Count();
                Assert.AreEqual(player.EngineBay.Count, distinctIds,
                    $"Player {player.UserId} has duplicate modifier ids in their bay.");
            }

            // Eras advanced to the configured count.
            Assert.IsTrue(state.CurrentEra >= eraCount,
                $"Expected at least {eraCount} eras but reached {state.CurrentEra}.");
        }

        // Steps a live Intermission through its timed sub-phases (Optimization → SniperBan; cards
        // are dealt and bays expanded instantly on entry) by ticking well past each sub-phase's
        // timer, until the FSM hands back to RoundState (or GameOver). No optimization/ban commands
        // are issued, so the deal defaults and the SniperBan timeout-draw fallback are exercised.
        private static void StepIntermissionToCompletion(AlphaChainGameEngine engine, AlphaChainGameState state)
        {
            var t0 = DateTimeOffset.UtcNow;
            for (int step = 1; step <= 10 && state.Phase == AlphaChainGamePhase.Intermission; step++)
                engine.Tick(state.Context!, t0.AddSeconds(step * 30));

            Assert.AreNotEqual(AlphaChainGamePhase.Intermission, state.Phase,
                "Intermission failed to complete within the tick budget.");
        }

        // Builds the next chained word: starts with the required letter (or a safe letter when
        // the chain is free), ends with a fixed non-banned letter so the chain always continues,
        // and encodes a monotonic counter into the middle so every word is unique and contains
        // no banned letter.
        private static string NextWord(char? requiredStart, char? banned, ref int counter)
        {
            char bannedChar = banned ?? '\0';

            // Mid alphabet: every letter b..z except the banned letter (a is reserved out so a
            // "free" start is always safe).
            var alphabet = new StringBuilder();
            for (char c = 'b'; c <= 'z'; c++)
                if (c != bannedChar) alphabet.Append(c);
            string alpha = alphabet.ToString();

            char endLetter = bannedChar == 'e' ? 'o' : 'e';
            char startLetter = requiredStart ?? (bannedChar == 'b' ? 'c' : 'b');

            // Encode the counter in base-|alpha| over the safe alphabet (at least one char).
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
