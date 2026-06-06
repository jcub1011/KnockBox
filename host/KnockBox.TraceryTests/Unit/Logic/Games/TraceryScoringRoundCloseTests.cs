using System.Linq;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.WordService.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.Tracery.Tests.Unit.Logic.Games
{
    /// <summary>
    /// Covers the scoring pass inside <see cref="TraceryGameEngine.CompleteRound"/> (Milestone 06):
    /// unique-find resolved against every player's banks post-lock, and cumulative scores that
    /// accumulate across rounds. Words are banked directly into the player states (bypassing grid
    /// validation) so the scenarios are fully controlled — a bare mock word service is enough since
    /// these tests never need a real board.
    /// </summary>
    [TestClass]
    public class TraceryScoringRoundCloseTests
    {
        private TraceryGameEngine _engine = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _engine = new TraceryGameEngine(
                new Mock<IWordListService>().Object,
                new RandomNumberService(),
                NullLogger<TraceryGameEngine>.Instance,
                NullLogger<TraceryGameState>.Instance);
            _host = UserFactory.Create("Host", Guid.NewGuid());
        }

        // ── Unique vs shared ────────────────────────────────────────────────

        [TestMethod]
        public async Task CompleteRound_AppliesMultiplierOnlyToUniqueFinds()
        {
            var (state, p1, p2) = await StartTwoPlayerRound();

            // p1 banks a word nobody else finds ("quartz") plus one shared word ("table");
            // p2 banks only the shared word. "quartz" is unique; "table" is found by both.
            state.Execute(() =>
            {
                Bank(state, p1, "quartz");
                Bank(state, p1, "table");
                Bank(state, p2, "table");
            });

            state.Execute(() => _engine.CompleteRound(state));

            var outcomes = LastOutcomes(state);
            var o1 = outcomes.Single(o => o.UserId == p1.Id);
            var o2 = outcomes.Single(o => o.UserId == p2.Id);

            // quartz unique → (6+3+10)×1.5 = 29; table shared → 5+1 = 6.
            Assert.AreEqual(29, WordPoints(o1, "quartz"));
            Assert.IsTrue(o1.WordScores.Single(w => w.Word == "quartz").IsUnique);
            Assert.AreEqual(6, WordPoints(o1, "table"));
            Assert.IsFalse(o1.WordScores.Single(w => w.Word == "table").IsUnique);
            Assert.AreEqual(35, o1.PointsAwarded);

            // Same shared word gets no multiplier for p2 either.
            Assert.AreEqual(6, WordPoints(o2, "table"));
            Assert.IsFalse(o2.WordScores.Single(w => w.Word == "table").IsUnique);
            Assert.AreEqual(6, o2.PointsAwarded);
        }

        // ── Cumulative across a scripted 2-round match ──────────────────────

        [TestMethod]
        public async Task CompleteRound_AccumulatesCumulativeScores_AcrossRounds()
        {
            var (state, p1, p2) = await StartTwoPlayerRound();

            // Round 1: each player's word is unique to them.
            state.Execute(() =>
            {
                Bank(state, p1, "table"); // 6 ×1.5 = 9
                Bank(state, p2, "rate");  // 4 ×1.5 = 6
            });
            state.Execute(() => _engine.CompleteRound(state));

            // Round 2: EnterPlaying clears the banks; bank a fresh, again-unique word each.
            state.Execute(() => _engine.EnterPlaying(state));
            state.Execute(() =>
            {
                Bank(state, p1, "rate");   // 4 ×1.5 = 6
                Bank(state, p2, "quartz"); // 19 ×1.5 = 29
            });
            state.Execute(() => _engine.CompleteRound(state));

            // Cumulative equals the sum of each round's awarded points.
            Assert.IsTrue(state.TryGetPlayerState(p1.Id, out var ps1));
            Assert.IsTrue(state.TryGetPlayerState(p2.Id, out var ps2));
            Assert.AreEqual(15, ps1.CumulativeScore); // 9 + 6
            Assert.AreEqual(35, ps2.CumulativeScore); // 6 + 29

            // …and the round results agree, per player, with the running totals.
            foreach (var player in new[] { p1, p2 })
            {
                int summed = state.RoundResults
                    .SelectMany(r => r.Outcomes)
                    .Where(o => o.UserId == player.Id)
                    .Sum(o => o.PointsAwarded);
                Assert.IsTrue(state.TryGetPlayerState(player.Id, out var ps));
                Assert.AreEqual(ps.CumulativeScore, summed);

                // The last round's snapshot also matches the live cumulative.
                var lastOutcome = state.RoundResults[^1].Outcomes.Single(o => o.UserId == player.Id);
                Assert.AreEqual(ps.CumulativeScore, lastOutcome.CumulativeScore);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        // Starts a 2-player match (host observes) with long timers — no scheduled callback fires
        // before the synchronous assertions — and drives into the first Playing round.
        private async Task<(TraceryGameState State, User P1, User P2)> StartTwoPlayerRound()
        {
            var create = await _engine.CreateStateAsync(_host);
            Assert.IsTrue(create.TryGetSuccess(out var created));
            var state = (TraceryGameState)created!;

            var p1 = UserFactory.Create("P1", Guid.NewGuid());
            var p2 = UserFactory.Create("P2", Guid.NewGuid());
            Assert.IsTrue(state.RegisterPlayer(p1).IsSuccess);
            Assert.IsTrue(state.RegisterPlayer(p2).IsSuccess);

            state.UpdateSettings(s => s with
            {
                RoundTimer = TimeSpan.FromMinutes(5),
                TransitionDuration = TimeSpan.FromMinutes(5)
            });

            await _engine.StartAsync(_host, state);
            state.Execute(() => _engine.EnterPlaying(state));
            return (state, p1, p2);
        }

        // Banks a word directly into a player's state. Caller must already hold the execute lock.
        private static void Bank(TraceryGameState state, User player, string word)
            => state.CreatePlayerState(player.Id).Bank(new TracedWord(word, [0]));

        private static System.Collections.Immutable.ImmutableArray<TraceryPlayerRoundOutcome> LastOutcomes(
            TraceryGameState state) => state.RoundResults[^1].Outcomes;

        private static int WordPoints(TraceryPlayerRoundOutcome outcome, string word)
            => outcome.WordScores.Single(w => w.Word == word).Points;
    }
}
