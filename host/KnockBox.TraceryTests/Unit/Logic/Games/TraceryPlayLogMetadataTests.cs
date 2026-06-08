using System.Linq;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.WordService.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.Tracery.Tests.Unit.Logic.Games
{
    /// <summary>
    /// Covers <see cref="TraceryPlayLogMetadata.Build"/> — the per-user play-log metadata for a
    /// finished match. Drives a real 2-player match into <see cref="GamePhase.FinalStandings"/> so
    /// the standings (frozen roster + cumulative scores) match what the final-standings view shows.
    /// </summary>
    [TestClass]
    public class TraceryPlayLogMetadataTests
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

        [TestMethod]
        public async Task Build_ForParticipant_EmitsMatchAndPersonalKeys()
        {
            var (state, p1, p2) = await StartTerminalMatch();

            // p1 = 9, p2 = 6 (see StartTerminalMatch), so p1 wins and p2 places 2nd.
            var meta = TraceryPlayLogMetadata.Build(state, p2.Id);

            Assert.AreEqual("P1", meta["Winner"]);
            Assert.AreEqual("1", meta["Rounds"]);
            Assert.AreEqual("2", meta["Players"]);
            Assert.AreEqual("6", meta["My Score"]);
            Assert.AreEqual("2 / 2", meta["Placement"]);
        }

        [TestMethod]
        public async Task Build_ForWinner_ReportsFirstPlacement()
        {
            var (state, p1, _) = await StartTerminalMatch();

            var meta = TraceryPlayLogMetadata.Build(state, p1.Id);

            Assert.AreEqual("9", meta["My Score"]);
            Assert.AreEqual("1 / 2", meta["Placement"]);
        }

        [TestMethod]
        public async Task Build_ForNonParticipant_OmitsPersonalKeys()
        {
            var (state, _, _) = await StartTerminalMatch();

            // The observing host is not a participant in this 2-player match, so only the
            // match-level keys are present.
            var meta = TraceryPlayLogMetadata.Build(state, _host.Id);

            Assert.AreEqual("P1", meta["Winner"]);
            Assert.IsFalse(meta.ContainsKey("My Score"));
            Assert.IsFalse(meta.ContainsKey("Placement"));
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        // Drives a 2-player match (host observes) through one scored round and into the terminal
        // FinalStandings phase. p1 banks a unique "table" (6 ×1.5 = 9), p2 a unique "rate"
        // (4 ×1.5 = 6), so the standings are deterministic.
        private async Task<(TraceryGameState State, User P1, User P2)> StartTerminalMatch()
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
            state.Execute(() =>
            {
                state.CreatePlayerState(p1.Id).Bank(new TracedWord("table", [0]));
                state.CreatePlayerState(p2.Id).Bank(new TracedWord("rate", [0]));
            });
            state.Execute(() => _engine.CompleteRound(state));

            // The metadata helper only fires once the page reaches FinalStandings; reflect that.
            state.Execute(() => state.Phase = GamePhase.FinalStandings);
            return (state, p1, p2);
        }
    }
}
