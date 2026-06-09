using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.Tracery.Contracts;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.Tracery.Tests.Unit.Projection
{
    /// <summary>
    /// Projection security + serialization. The per-recipient view must never carry another
    /// player's in-progress banked words, nor the server's full findable-word answer key, and it
    /// must round-trip through both the hub's reflection serializer and the client's source-gen
    /// context (the real WASM path). Uses the same fixed 3×3 board as the submit-trace tests:
    ///   T(0) A(1) B(2) / R(3) C(4) E(5) / P(6) O(7) D(8)
    /// </summary>
    [TestClass]
    public class TraceryStateProjectorTests
    {
        private static TraceryGameEngine _engine = default!;

        // Hub wire format (matches GameViewCoordinator) + the source-gen context the client uses.
        private static readonly JsonSerializerOptions WireOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private static Grid MakeBoard() => new(3, 3, "tabrcepod");

        [ClassInitialize]
        public static void ClassInit(TestContext _)
        {
            var svc = new WordListService(NullLogger<WordListService>.Instance);
            _engine = new TraceryGameEngine(
                svc, new RandomNumberService(),
                NullLogger<TraceryGameEngine>.Instance, NullLogger<TraceryGameState>.Instance);
        }

        [TestMethod]
        public async Task ProjectFor_DuringPlay_RevealsOnlyTheRecipientsOwnBanks()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var p1 = UserFactory.Create("P1", Guid.NewGuid());
            var (state, hostWord, p1Word) = await PlayingWithTwoBanksAsync(host, p1);

            var hostView = ProjectFor(state, host.Id);
            var p1View = ProjectFor(state, p1.Id);

            // Each player sees their own bank…
            CollectionAssert.AreEquivalent(new[] { hostWord }, hostView.MyBankedWords.Select(b => b.Word).ToList());
            CollectionAssert.AreEquivalent(new[] { p1Word }, p1View.MyBankedWords.Select(b => b.Word).ToList());

            // …and there is structurally no channel for the opponent's bank: the only banked-word
            // field is the recipient's own (MyBankedWords), the reveal-only board word set (the
            // answer key) is empty mid-round, and neither player is the host-observer here, so the
            // standings rail (banked COUNTS) is empty for both. (We can't substring-match the words:
            // a traced word's letters are a legitimate, public substring of the board letters.)
            Assert.AreEqual(0, hostView.RevealBoardWords.Count, "Answer key must not be projected during play.");
            Assert.AreEqual(0, p1View.RevealBoardWords.Count, "Answer key must not be projected during play.");
            Assert.AreEqual(0, hostView.HostBoardStandings.Count, "A participant must not receive opponents' counts.");
            Assert.AreEqual(0, p1View.HostBoardStandings.Count, "A participant must not receive opponents' counts.");
        }

        [TestMethod]
        public async Task ProjectFor_DuringPlay_HostBoardStandingsOnlyForTheObserver()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var p1 = UserFactory.Create("P1", Guid.NewGuid());
            var p2 = UserFactory.Create("P2", Guid.NewGuid());
            // Two non-host players + HostPlaysAlong off → host is the display-only observer.
            var (engine, state) = await StartIntoPlayingAsync(host, hostPlays: false, others: [p1, p2]);
            Assert.IsFalse(state.HostIsParticipant);

            var (word, path) = FirstFindable();
            engine.SubmitTrace(state, p1, path); // p1 banks one word

            var hostView = ProjectFor(state, host.Id);
            var p1View = ProjectFor(state, p1.Id);

            // The observing host gets per-participant banked COUNTS (the TraceryLiveStanding type has
            // no word field, so the words themselves never reach even the host display)…
            Assert.IsTrue(hostView.IsHostObserver);
            Assert.AreEqual(2, hostView.HostBoardStandings.Count);
            Assert.AreEqual(1, hostView.HostBoardStandings.Single(s => s.UserId == p1.Id).BankedCount);
            Assert.AreEqual(0, hostView.MyBankedWords.Count, "An observing host banks nothing.");

            // …and a competing player gets neither the standings rail nor opponents' banks.
            Assert.AreEqual(0, p1View.HostBoardStandings.Count);
            _ = word; // the banked word is asserted via the count above, not by string-matching the board
        }

        [TestMethod]
        public async Task ProjectFor_RoundTripsThroughHubAndSourceGen()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var p1 = UserFactory.Create("P1", Guid.NewGuid());
            var (state, hostWord, _) = await PlayingWithTwoBanksAsync(host, p1);

            var view = ProjectFor(state, host.Id);

            // Hub path: reflection serialize (string enums) → source-gen deserialize (the client's path).
            var json = JsonSerializer.Serialize(view, view.GetType(), WireOptions);
            var roundTripped = JsonSerializer.Deserialize(json, TraceryContractsJsonContext.Default.TraceryView);

            Assert.IsNotNull(roundTripped);
            Assert.AreEqual(GamePhase.Playing, roundTripped!.Phase);
            Assert.IsNotNull(roundTripped.Grid);
            Assert.AreEqual("tabrcepod", roundTripped.Grid!.Letters);
            Assert.AreEqual(3, roundTripped.Grid.Width);
            // The recipient's own bank survives the trim-safe path.
            CollectionAssert.AreEquivalent(new[] { hostWord }, roundTripped.MyBankedWords.Select(b => b.Word).ToList());
        }

        [TestMethod]
        public async Task ProjectFor_SearchMode_RoundTripsTheSharedList()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (engine, state) = await StartIntoPlayingAsync(host, hostPlays: true, others: null, searchMode: true);

            var view = ProjectFor(state, host.Id);
            Assert.IsTrue(view.SearchList.Count > 0, "Search mode should project a non-empty shared list.");

            var json = JsonSerializer.Serialize(view, view.GetType(), WireOptions);
            var roundTripped = JsonSerializer.Deserialize(json, TraceryContractsJsonContext.Default.TraceryView)!;
            CollectionAssert.AreEqual(view.SearchList.ToList(), roundTripped.SearchList.ToList());
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static TraceryView ProjectFor(TraceryGameState state, Guid recipientId)
            => (TraceryView)((IGameStateProjector)_engine).ProjectFor(state, recipientId)!;

        private static async Task<(TraceryGameState state, string hostWord, string p1Word)> PlayingWithTwoBanksAsync(
            User host, User p1)
        {
            // HostPlaysAlong so the host is a participant alongside p1 → two banking players.
            var (engine, state) = await StartIntoPlayingAsync(host, hostPlays: true, others: [p1]);

            var distinct = TwoDistinctFindable();
            engine.SubmitTrace(state, host, distinct.a.Path.ToArray());
            engine.SubmitTrace(state, p1, distinct.b.Path.ToArray());
            return (state, distinct.a.Word, distinct.b.Word);
        }

        private static async Task<(TraceryGameEngine engine, TraceryGameState state)> StartIntoPlayingAsync(
            User host, bool hostPlays, List<User>? others, bool searchMode = false)
        {
            var created = await _engine.CreateStateAsync(host);
            Assert.IsTrue(created.TryGetSuccess(out var s));
            var state = (TraceryGameState)s!;
            state.UpdateSettings(x => x with
            {
                RoundTimer = TimeSpan.FromMinutes(5),
                TransitionDuration = TimeSpan.FromMinutes(5),
                IntermissionDuration = TimeSpan.FromMinutes(5),
                MinWordLength = 3,
                HostPlaysAlong = hostPlays,
                Mode = searchMode ? GameMode.Search : GameMode.Standard,
            });
            if (others is not null)
                foreach (var p in others)
                    Assert.IsTrue(state.RegisterPlayer(p).IsSuccess);

            await _engine.StartAsync(host, state);
            state.Execute(() => _engine.EnterPlaying(state));
            // Pin a known board so paths are stable. In Search mode the list was drawn during
            // EnterPlaying from the randomly generated board, which is fine for the round-trip test.
            if (!searchMode)
                state.Execute(() => state.CurrentGrid = MakeBoard());
            return (_engine, state);
        }

        private static (string word, int[] path) FirstFindable()
        {
            var found = _engine.GetSolver(WordPoolMode.FullDictionary).Solve(MakeBoard(), minWordLength: 3);
            var entry = found.Values.First();
            return (entry.Word, entry.Path.ToArray());
        }

        private static (TracedWord a, TracedWord b) TwoDistinctFindable()
        {
            var found = _engine.GetSolver(WordPoolMode.FullDictionary).Solve(MakeBoard(), minWordLength: 3).Values.ToList();
            return (found[0], found[1]);
        }
    }
}
