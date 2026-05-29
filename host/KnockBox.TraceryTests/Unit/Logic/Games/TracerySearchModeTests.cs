using KnockBox.Tracery.Models;
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
    /// Covers the Search game mode: the shared per-round target list, the list-only submission
    /// filter, finishing-order tracking with early round end, and the flat-per-word + placement
    /// bonus scoring. Uses the real <see cref="WordListService"/> so the engine's solver checks
    /// against the production dictionary; most tests pin a fixed 3×3 board + a hand-picked search
    /// list so word membership and trace paths are stable:
    ///   T(0) A(1) B(2)
    ///   R(3) C(4) E(5)
    ///   P(6) O(7) D(8)
    /// </summary>
    [TestClass]
    public class TracerySearchModeTests
    {
        // One engine (hence one built trie) shared by every test — the solver caches it.
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

        // ── Search-list generation ──────────────────────────────────────────

        [TestMethod]
        public async Task EnterPlaying_SearchMode_BuildsListOfConfiguredSize_FromBoardWords()
        {
            var host = UserFactory.Create("Host", "host1");
            var state = await StartGeneratedSearchAsync(host, listSize: 5);

            Assert.IsTrue(state.BoardFindableWords.Count >= 5, "Test assumes the board offers ≥5 words.");
            Assert.AreEqual(5, state.SearchList.Length);
            // Every target is an actual board word, and they're distinct.
            Assert.IsTrue(state.SearchList.All(w => state.BoardFindableWords.ContainsKey(w)));
            Assert.AreEqual(state.SearchList.Length, state.SearchList.Distinct().Count());
        }

        [TestMethod]
        public async Task EnterPlaying_SearchMode_ClampsListToFindableCount()
        {
            var host = UserFactory.Create("Host", "host1");
            // Ask for far more words than any board can offer.
            var state = await StartGeneratedSearchAsync(host, listSize: 100_000);

            Assert.AreEqual(state.BoardFindableWords.Count, state.SearchList.Length);
        }

        [TestMethod]
        public async Task EnterPlaying_StandardMode_LeavesSearchListEmpty()
        {
            var host = UserFactory.Create("Host", "host1");
            var created = await _engine.CreateStateAsync(host);
            var state = (TraceryGameState)created.Value!;
            state.UpdateSettings(s => s with
            {
                Mode = GameMode.Standard,
                RoundTimer = TimeSpan.FromMinutes(5),
                TransitionDuration = TimeSpan.FromMinutes(5)
            });
            await _engine.StartAsync(host, state);
            state.Execute(() => _engine.EnterPlaying(state));

            Assert.IsTrue(state.SearchList.IsEmpty);
        }

        // ── List-only banking ───────────────────────────────────────────────

        [TestMethod]
        public async Task SubmitTrace_SearchMode_BanksListedWord_RejectsUnlistedWord()
        {
            var host = UserFactory.Create("Host", "host1");
            var (state, list, offList) = await StartPinnedSearchAsync(host, listWords: 2);

            // A valid word that isn't a target is ignored — rejected and not banked.
            var rejected = _engine.SubmitTrace(state, host, offList.Path.ToArray());
            Assert.IsTrue(rejected.IsFailure);
            Assert.IsTrue(rejected.TryGetFailure(out var err));
            StringAssert.Contains(err.PublicMessage, "search list");
            Assert.IsTrue(state.TryGetPlayerState(host.Id, out var ps));
            Assert.AreEqual(0, ps.BankedWords.Count);

            // A listed word banks normally.
            var accepted = _engine.SubmitTrace(state, host, list[0].Path.ToArray());
            Assert.IsTrue(accepted.IsSuccess);
            Assert.IsTrue(ps.HasBanked(list[0].Word));
            Assert.AreEqual(1, ps.BankedWords.Count);
        }

        // ── Finishing order + early round end ───────────────────────────────

        [TestMethod]
        public async Task SubmitTrace_SearchMode_AssignsRanksInOrder_AndEndsRoundWhenAllComplete()
        {
            var host = UserFactory.Create("Host", "host1");
            var p1 = UserFactory.Create("P1", "p1");
            var p2 = UserFactory.Create("P2", "p2");
            // Two players + host-observer → both are the participants.
            var (state, list, _) = await StartPinnedSearchAsync(host, listWords: 2, others: [p1, p2]);

            // p1 finds both targets first → places 1st, round still running (p2 unfinished).
            foreach (var w in list) Assert.IsTrue(_engine.SubmitTrace(state, p1, w.Path.ToArray()).IsSuccess);
            Assert.IsTrue(state.TryGetPlayerState(p1.Id, out var ps1));
            Assert.AreEqual(1, ps1.CompletionRank);
            Assert.AreEqual(GamePhase.Playing, state.Phase);

            // p2 finishes → places 2nd, and the round ends early (no waiting out the clock).
            foreach (var w in list) _engine.SubmitTrace(state, p2, w.Path.ToArray());
            Assert.IsTrue(state.TryGetPlayerState(p2.Id, out var ps2));
            Assert.AreEqual(2, ps2.CompletionRank);
            Assert.AreEqual(GamePhase.Reveal, state.Phase);
            Assert.IsFalse(state.IsRoundActive);
        }

        // ── Scoring: flat per word + placement bonus that scales with player count ──

        [TestMethod]
        public async Task CompleteRound_SearchMode_ScoresFlatWords_AndPlacementBonusByRank()
        {
            var host = UserFactory.Create("Host", "host1");
            var p1 = UserFactory.Create("P1", "p1");
            var p2 = UserFactory.Create("P2", "p2");
            var p3 = UserFactory.Create("P3", "p3");
            const int unit = 10;
            var (state, list, _) = await StartPinnedSearchAsync(host, listWords: 2, others: [p1, p2, p3], placementUnit: unit);
            int listLengthSum = list.Sum(w => w.Word.Length);

            // p1 then p2 complete (ranks 1 and 2); p3 finds only the first target (no rank).
            foreach (var w in list) _engine.SubmitTrace(state, p1, w.Path.ToArray());
            foreach (var w in list) _engine.SubmitTrace(state, p2, w.Path.ToArray());
            _engine.SubmitTrace(state, p3, list[0].Path.ToArray());

            // Two of three finished → round did NOT auto-end; close it on the timer path.
            Assert.AreEqual(GamePhase.Playing, state.Phase);
            state.Execute(() => _engine.CompleteRound(state));

            var outcomes = state.RoundResults[^1].Outcomes;
            var o1 = outcomes.Single(o => o.UserId == p1.Id);
            var o2 = outcomes.Single(o => o.UserId == p2.Id);
            var o3 = outcomes.Single(o => o.UserId == p3.Id);

            const int participantCount = 3;
            // Placement bonus scales with player count and decreases per place.
            Assert.AreEqual(unit * participantCount, o1.CompletionBonus);       // 1st: unit × P
            Assert.AreEqual(unit * (participantCount - 1), o2.CompletionBonus); // 2nd: unit × (P-1)
            Assert.AreEqual(0, o3.CompletionBonus);                             // didn't finish → none

            // Flat word points = summed lengths (no length/rare/unique bonuses), plus the bonus.
            Assert.AreEqual(listLengthSum + o1.CompletionBonus, o1.PointsAwarded);
            Assert.AreEqual(listLengthSum + o2.CompletionBonus, o2.PointsAwarded);
            Assert.AreEqual(list[0].Word.Length, o3.PointsAwarded); // one word, no bonus

            // Each per-word score is purely the word's length — no bonus layers applied.
            Assert.IsTrue(o1.WordScores.All(ws => ws.Points == ws.Word.Length && ws.LengthBonus == 0 && ws.RareLetterBonus == 0));

            Assert.AreEqual(1, o1.CompletionRank);
            Assert.AreEqual(2, o2.CompletionRank);
            Assert.IsNull(o3.CompletionRank);
            Assert.AreEqual(2, o1.SearchListSize);
            Assert.AreEqual(1, o3.WordsFound);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        // Starts a Search match into Playing on the engine's randomly generated board (used by the
        // list-generation tests, which assert against the real solve).
        private static async Task<TraceryGameState> StartGeneratedSearchAsync(User host, int listSize)
        {
            var created = await _engine.CreateStateAsync(host);
            var state = (TraceryGameState)created.Value!;
            state.UpdateSettings(s => s with
            {
                Mode = GameMode.Search,
                SearchListSize = listSize,
                RoundTimer = TimeSpan.FromMinutes(5),
                TransitionDuration = TimeSpan.FromMinutes(5)
            });
            await _engine.StartAsync(host, state);
            state.Execute(() => _engine.EnterPlaying(state));
            return state;
        }

        // Starts a Search match into Playing, then pins a deterministic board + search list so trace
        // paths and list membership are stable. Returns the list words (with paths) plus one extra
        // findable word that is deliberately NOT on the list (for the rejection test).
        private static async Task<(TraceryGameState state, List<TracedWord> list, TracedWord offList)> StartPinnedSearchAsync(
            User host, int listWords, List<User>? others = null, int placementUnit = 10)
        {
            var created = await _engine.CreateStateAsync(host);
            var state = (TraceryGameState)created.Value!;
            state.UpdateSettings(s => s with
            {
                Mode = GameMode.Search,
                MinWordLength = 3,
                SearchListSize = listWords,
                SearchPlacementBonusUnit = placementUnit,
                RoundTimer = TimeSpan.FromMinutes(5),
                TransitionDuration = TimeSpan.FromMinutes(5)
            });
            if (others is not null)
                foreach (var p in others)
                    Assert.IsTrue(state.RegisterPlayer(p).IsSuccess);

            await _engine.StartAsync(host, state);
            state.Execute(() => _engine.EnterPlaying(state));

            var board = MakeBoard();
            var solved = _engine.GetSolver(WordPoolMode.FullDictionary).Solve(board, minWordLength: 3);
            var allWords = solved.Values.ToList();
            Assert.IsTrue(allWords.Count > listWords, "Test board must offer more words than the list size.");

            var list = allWords.Take(listWords).ToList();
            var offList = allWords[listWords]; // a findable word intentionally left off the list

            state.Execute(() =>
            {
                state.CurrentGrid = board;
                state.BoardFindableWords = solved;
                state.FindableWords = solved;
                state.SearchList = [.. list.Select(w => w.Word)];
                state.SearchCompletionsThisRound = 0;
            });

            return (state, list, offList);
        }
    }
}
