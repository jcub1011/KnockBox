using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.Tracery.Tests.Unit.Logic.Games
{
    /// <summary>
    /// Covers <see cref="TraceryGameEngine.SubmitTrace"/> (Milestone 05): the authoritative
    /// validate-and-bank path shared by drag and tap input. Uses the real
    /// <see cref="WordListService"/> so the engine's solver checks against the production
    /// dictionary; the board is a fixed 3×3 so cell ids and paths are easy to reason about:
    ///   T(0) A(1) B(2)
    ///   R(3) C(4) E(5)
    ///   P(6) O(7) D(8)
    /// </summary>
    [TestClass]
    public class TracerySubmitTraceTests
    {
        // One engine (hence one built trie) shared by every test — GetSolver caches it, so the
        // ~386k-word load is paid once for the class rather than per test.
        private static TraceryGameEngine _engine = default!;

        private static Grid MakeBoard() => new(3, 3, "tabrcepod");

        [ClassInitialize]
        public static void ClassInit(TestContext _)
        {
            var svc = new WordListService(NullLogger<WordListService>.Instance);
            _engine = new TraceryGameEngine(
                svc, new RandomNumberService(),
                NullLogger<TraceryGameEngine>.Instance, NullLogger<TraceryGameState>.Instance);
        }

        // ── Happy path ──────────────────────────────────────────────────────

        [TestMethod]
        public async Task SubmitTrace_ValidWord_BanksIt()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (engine, state) = await StartIntoPlayingAsync(host);
            var (word, path) = FirstFindable();

            var result = engine.SubmitTrace(state, host, path);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(state.TryGetPlayerState(host.Id, out var ps));
            Assert.IsTrue(ps.HasBanked(word), $"Expected \"{word}\" to be banked.");
            Assert.AreEqual(1, ps.BankedWords.Count);
        }

        // ── Parametrized rejections (path-only) ─────────────────────────────

        [TestMethod]
        [DataRow(new[] { 0, 3 }, "too short", DisplayName = "too short")]
        [DataRow(new[] { 4, 1, 8 }, "touching", DisplayName = "non-adjacent jump")]
        [DataRow(new[] { 4, 1, 4 }, "twice", DisplayName = "self-intersecting")]
        [DataRow(new[] { 6, 3, 0 }, "isn't a word", DisplayName = "not a word (prt)")]
        public async Task SubmitTrace_InvalidPath_RejectsWithReason(int[] path, string fragment)
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (engine, state) = await StartIntoPlayingAsync(host);

            var result = engine.SubmitTrace(state, host, path);

            Assert.IsTrue(result.IsFailure);
            Assert.IsTrue(result.TryGetFailure(out var err));
            StringAssert.Contains(err.PublicMessage, fragment,
                $"Expected public message to mention \"{fragment}\" but got \"{err.PublicMessage}\".");
            // A rejected trace never banks.
            Assert.IsTrue(state.TryGetPlayerState(host.Id, out var ps));
            Assert.AreEqual(0, ps.BankedWords.Count);
        }

        // ── State-based rejections ──────────────────────────────────────────

        [TestMethod]
        public async Task SubmitTrace_AfterRoundEnds_IsRejected()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (engine, state) = await StartIntoPlayingAsync(host);
            var (_, path) = FirstFindable();

            // Timer expiry / completion closes the input gate (Milestone 04).
            state.Execute(() => engine.CompleteRound(state));

            var result = engine.SubmitTrace(state, host, path);

            Assert.IsTrue(result.IsFailure);
            Assert.IsTrue(result.TryGetFailure(out var err));
            StringAssert.Contains(err.PublicMessage, "not active");
        }

        [TestMethod]
        public async Task SubmitTrace_ByObservingHost_IsRejected()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var p1 = UserFactory.Create("P1", Guid.NewGuid());
            var p2 = UserFactory.Create("P2", Guid.NewGuid());
            // Others present + HostPlaysAlong off → host is the display-only observer.
            var (engine, state) = await StartIntoPlayingAsync(host, new() { p1, p2 });
            Assert.IsFalse(state.HostIsParticipant);
            var (_, path) = FirstFindable();

            var result = engine.SubmitTrace(state, host, path);

            Assert.IsTrue(result.IsFailure);
            Assert.IsTrue(result.TryGetFailure(out var err));
            StringAssert.Contains(err.PublicMessage, "observing");
        }

        [TestMethod]
        public async Task SubmitTrace_ByStranger_IsRejected()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (engine, state) = await StartIntoPlayingAsync(host);
            var stranger = UserFactory.Create("Nobody", Guid.NewGuid());
            var (_, path) = FirstFindable();

            var result = engine.SubmitTrace(state, stranger, path);

            Assert.IsTrue(result.IsFailure);
            Assert.IsTrue(result.TryGetFailure(out var err));
            StringAssert.Contains(err.PublicMessage, "not a participant");
            // The stranger must not have been materialized into a player state.
            Assert.IsFalse(state.TryGetPlayerState(stranger.Id, out _));
        }

        // ── Duplicate-bank no-op ────────────────────────────────────────────

        [TestMethod]
        public async Task SubmitTrace_AlreadyBankedWord_IsSilentNoOpSuccess()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (engine, state) = await StartIntoPlayingAsync(host);
            var (word, path) = FirstFindable();

            var first = engine.SubmitTrace(state, host, path);
            var second = engine.SubmitTrace(state, host, path);

            Assert.IsTrue(first.IsSuccess);
            Assert.IsTrue(second.IsSuccess, "Re-banking a known word is a no-op success, not a failure.");
            Assert.IsTrue(state.TryGetPlayerState(host.Id, out var ps));
            Assert.AreEqual(1, ps.BankedWords.Count, "The duplicate must not add a second bank entry.");
            Assert.IsTrue(ps.HasBanked(word));
        }

        // ── Cell reuse across words ─────────────────────────────────────────

        [TestMethod]
        public async Task SubmitTrace_DistinctWordsReusingCells_BothBank()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (engine, state) = await StartIntoPlayingAsync(host);

            // Two findable words whose paths share at least one cell — proves cells are not
            // consumed by the first word and remain available for the second.
            var (a, b) = TwoFindableSharingACell();

            var firstResult = engine.SubmitTrace(state, host, a.Path);
            var secondResult = engine.SubmitTrace(state, host, b.Path);

            Assert.IsTrue(firstResult.IsSuccess);
            Assert.IsTrue(secondResult.IsSuccess);
            Assert.IsTrue(state.TryGetPlayerState(host.Id, out var ps));
            Assert.AreEqual(2, ps.BankedWords.Count);
            Assert.IsTrue(ps.HasBanked(a.Word));
            Assert.IsTrue(ps.HasBanked(b.Word));
            Assert.IsTrue(a.Path.Intersect(b.Path).Any(), "Test setup expects the two words to share a cell.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static async Task<(TraceryGameEngine engine, TraceryGameState state)> StartIntoPlayingAsync(
            User host, List<User>? others = null)
        {
            var created = await _engine.CreateStateAsync(host);
            Assert.IsTrue(created.TryGetSuccess(out var s));
            var state = (TraceryGameState)s!;
            // Long timers so no scheduled callback fires mid-test; min length 3 matches the board.
            state.UpdateSettings(x => x with
            {
                RoundTimer = TimeSpan.FromMinutes(5),
                TransitionDuration = TimeSpan.FromMinutes(5),
                MinWordLength = 3
            });
            if (others is not null)
                foreach (var p in others)
                    Assert.IsTrue(state.RegisterPlayer(p).IsSuccess);

            await _engine.StartAsync(host, state);
            state.Execute(() => _engine.EnterPlaying(state));
            // Replace the randomly generated board with the fixed test board so paths are stable.
            state.Execute(() => state.CurrentGrid = MakeBoard());
            return (_engine, state);
        }

        // The engine's own solver guarantees these words exist in the production dictionary and
        // that the returned path is legal — no reliance on which specific words are present.
        private static (string word, int[] path) FirstFindable()
        {
            var solver = _engine.GetSolver(WordPoolMode.FullDictionary);
            var found = solver.Solve(MakeBoard(), minWordLength: 3);
            var entry = found.Values.First();
            return (entry.Word, entry.Path.ToArray());
        }

        private static (TracedWord a, TracedWord b) TwoFindableSharingACell()
        {
            var solver = _engine.GetSolver(WordPoolMode.FullDictionary);
            var found = solver.Solve(MakeBoard(), minWordLength: 3).Values.ToList();
            for (int i = 0; i < found.Count; i++)
                for (int j = i + 1; j < found.Count; j++)
                    if (found[i].Path.Intersect(found[j].Path).Any())
                        return (found[i], found[j]);
            throw new InvalidOperationException("Expected two findable words sharing a cell on the test board.");
        }
    }
}
