using System.Collections.Immutable;
using System.Linq;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Services.Logic.Dictionary;

namespace KnockBox.Tracery.Tests.Unit.Logic
{
    /// <summary>
    /// Covers <see cref="RevealBuilder"/> (Milestone 07): the pure projection of a round's
    /// findable set + scored <see cref="RoundResult"/> into the host reveal beats. Findable sets
    /// are built either synthetically or from the real solver over a fixed grid; banks are scripted
    /// through <see cref="TraceryScorer"/> exactly as the engine scores them, so the assertions
    /// pin both the selection rules (longest, highest-scoring, ties) and the derived values
    /// (nobody-found set difference, rarest letters, theoretical max).
    /// </summary>
    [TestClass]
    public class RevealBuilderTests
    {
        private static readonly TracerySettings Default = new();

        // ── Longest word ────────────────────────────────────────────────────

        [TestMethod]
        public void LongestWord_PicksTheLongestBankedWord_AndCarriesItsPath()
        {
            var findable = Findable("trace", "cat");
            var round = ScoreRound(1, Default,
                ("Alice", Guid.NewGuid(), new[] { "trace", "cat" }));

            var reveal = RevealBuilder.Build(findable, findable, round, Default);

            Assert.IsNotNull(reveal.LongestWord);
            Assert.AreEqual("trace", reveal.LongestWord!.Word);
            Assert.AreEqual(5, reveal.LongestWord.Length);
            CollectionAssert.AreEqual(new[] { "Alice" }, reveal.LongestWord.Finders.ToArray());
            // The representative path is sourced from the findable set.
            CollectionAssert.AreEqual(findable["trace"].Path.ToArray(), reveal.LongestWord.Path.ToArray());
        }

        [TestMethod]
        public void LongestWord_SharedByTwoPlayers_ListsBothFinders()
        {
            var findable = Findable("trace", "cat");
            var round = ScoreRound(1, Default,
                ("Bob", Guid.NewGuid(), new[] { "trace" }),
                ("Alice", Guid.NewGuid(), new[] { "trace", "cat" }));

            var reveal = RevealBuilder.Build(findable, findable, round, Default);

            Assert.AreEqual("trace", reveal.LongestWord!.Word);
            // Finders are ordered alphabetically, regardless of player order.
            CollectionAssert.AreEqual(new[] { "Alice", "Bob" }, reveal.LongestWord.Finders.ToArray());
        }

        [TestMethod]
        public void LongestWord_TieOnLength_BreaksByPointsThenAlphabetical()
        {
            // "table" and "trace" are both 5 letters and both unique → equal points (9 each),
            // so the alphabetical tie-break selects "table".
            var findable = Findable("table", "trace");
            var round = ScoreRound(1, Default,
                ("Alice", Guid.NewGuid(), new[] { "table" }),
                ("Bob", Guid.NewGuid(), new[] { "trace" }));

            var reveal = RevealBuilder.Build(findable, findable, round, Default);

            Assert.AreEqual("table", reveal.LongestWord!.Word);
        }

        // ── Highest-scoring word ────────────────────────────────────────────

        [TestMethod]
        public void HighestScoringWord_PicksMaxPoints_WithUniqueFlagAndFinder()
        {
            // quartz unique: (6 + 3 + 10) × 1.5 = 29; table shared: 6. quartz wins.
            var findable = Findable("quartz", "table");
            var round = ScoreRound(1, Default,
                ("Alice", Guid.NewGuid(), new[] { "quartz", "table" }),
                ("Bob", Guid.NewGuid(), new[] { "table" }));

            var reveal = RevealBuilder.Build(findable, findable, round, Default);

            Assert.IsNotNull(reveal.HighestScoringWord);
            Assert.AreEqual("quartz", reveal.HighestScoringWord!.Word);
            Assert.AreEqual(29, reveal.HighestScoringWord.Points);
            Assert.IsTrue(reveal.HighestScoringWord.IsUnique);
            CollectionAssert.AreEqual(new[] { "Alice" }, reveal.HighestScoringWord.Finders.ToArray());
        }

        // ── Words nobody found (sourced from the solver) ────────────────────

        [TestMethod]
        public void WordsNobodyFound_IsTheSolverSetMinusBanked_RichestFirst()
        {
            // Fixed 3×3 board (solver-test board); findable set comes straight from the solver.
            //   T(0) A(1) B(2)
            //   R(3) C(4) E(5)
            //   P(6) O(7) D(8)
            var grid = new Grid(3, 3, "tabrcepod");
            var trie = TraceryTrie.FromWords("trace", "cab", "car", "ace", "cod", "bat", "bar", "ear");
            var findable = new TracerySolver(trie).Solve(grid, minWordLength: 3);

            // Alice banks two of the eight findable words; the rest are "nobody found".
            var round = ScoreRound(1, Default, ("Alice", Guid.NewGuid(), new[] { "cab", "car" }));

            var reveal = RevealBuilder.Build(findable, findable, round, Default);

            var nobody = reveal.WordsNobodyFound.Select(m => m.Word).ToArray();
            // Exactly the solver set minus the banked words (a pure set difference).
            CollectionAssert.AreEquivalent(
                new[] { "trace", "ace", "cod", "bat", "bar", "ear" }, nobody);
            CollectionAssert.DoesNotContain(nobody, "cab");
            CollectionAssert.DoesNotContain(nobody, "car");
            // "trace" (would-be 9) outranks every 3-letter word (would-be 5), so it leads.
            Assert.AreEqual("trace", nobody[0]);
            Assert.AreEqual(9, reveal.WordsNobodyFound[0].WouldBeScore);
        }

        // ── Rarest letters ──────────────────────────────────────────────────

        [TestMethod]
        public void RarestLetters_SurfacesHighestValueLetters_FromBankedWords()
        {
            var findable = Findable("quartz", "milky");
            var round = ScoreRound(1, Default, ("Alice", Guid.NewGuid(), new[] { "quartz", "milky" }));

            var reveal = RevealBuilder.Build(findable, findable, round, Default);

            // Q(+5), Z(+5), K(+1), Y(+1) — ordered by bonus desc then letter.
            var letters = reveal.RarestLetters.Select(r => r.Letter).ToArray();
            CollectionAssert.AreEqual(new[] { 'Q', 'Z', 'K', 'Y' }, letters);
            Assert.AreEqual(5, reveal.RarestLetters[0].BonusValue);
            Assert.AreEqual("quartz", reveal.RarestLetters[0].ExampleWord);
        }

        [TestMethod]
        public void RarestLetters_Empty_WhenRareLetterBonusDisabled()
        {
            var settings = Default with { RareLetterBonusEnabled = false };
            var findable = Findable("quartz");
            var round = ScoreRound(1, settings, ("Alice", Guid.NewGuid(), new[] { "quartz" }));

            var reveal = RevealBuilder.Build(findable, findable, round, settings);

            Assert.AreEqual(0, reveal.RarestLetters.Length);
        }

        // ── Theoretical maximum ─────────────────────────────────────────────

        [TestMethod]
        public void TheoreticalMax_SumsTheWholeFindableSetAsUnique()
        {
            // trace: (5+1)×1.5 = 9; cat: (3+0)×1.5 = 4.5 → 5. Sum = 14.
            var findable = Findable("trace", "cat");
            var round = ScoreRound(1, Default, ("Alice", Guid.NewGuid(), new[] { "cat" }));

            var reveal = RevealBuilder.Build(findable, findable, round, Default);

            Assert.AreEqual(14, reveal.TheoreticalMax);
        }

        [TestMethod]
        public void TheoreticalMax_Null_WhenToggledOff()
        {
            var settings = Default with { ShowTheoreticalMax = false };
            var findable = Findable("trace", "cat");
            var round = ScoreRound(1, settings, ("Alice", Guid.NewGuid(), new[] { "cat" }));

            var reveal = RevealBuilder.Build(findable, findable, round, settings);

            Assert.IsNull(reveal.TheoreticalMax);
        }

        // ── Standings ───────────────────────────────────────────────────────

        [TestMethod]
        public void Standings_OrderedByCumulativeThenRoundPoints()
        {
            var round = new RoundResult
            {
                RoundNumber = 2,
                Outcomes =
                [
                    Outcome(Guid.NewGuid(), "Alice", points: 5, cumulative: 10),
                    Outcome(Guid.NewGuid(), "Bob", points: 5, cumulative: 25),
                    Outcome(Guid.NewGuid(), "Cara", points: 2, cumulative: 25),
                ]
            };

            var reveal = RevealBuilder.Build(Findable(), Findable(), round, Default);

            // Bob & Cara tie on cumulative (25); Bob's higher round points break the tie ahead of
            // Cara, and Alice (10) trails.
            CollectionAssert.AreEqual(
                new[] { "Bob", "Cara", "Alice" },
                reveal.Standings.Select(s => s.DisplayName).ToArray());
            Assert.AreEqual(5, reveal.Standings[0].RoundPoints);
            Assert.AreEqual(25, reveal.Standings[0].CumulativeScore);
        }

        // ── Empty round ─────────────────────────────────────────────────────

        [TestMethod]
        public void EmptyRound_NullWordBeats_ButBoardWordsStillSurface()
        {
            var findable = Findable("trace");
            var round = new RoundResult
            {
                RoundNumber = 1,
                Outcomes = [Outcome(Guid.NewGuid(), "Alice", points: 0, cumulative: 0)]
            };

            var reveal = RevealBuilder.Build(findable, findable, round, Default);

            Assert.IsNull(reveal.LongestWord);
            Assert.IsNull(reveal.HighestScoringWord);
            Assert.AreEqual(0, reveal.RarestLetters.Length);
            // The board's findable words are all "nobody found" when no one banked anything.
            CollectionAssert.AreEqual(new[] { "trace" }, reveal.WordsNobodyFound.Select(m => m.Word).ToArray());
            Assert.AreEqual(1, reveal.Standings.Length);
        }

        // ── Hybrid: distinct validation vs board sets ───────────────────────

        [TestMethod]
        public void Hybrid_NobodyFoundFromBoardSet_ButTheoreticalMaxFromValidationSet()
        {
            // Board (generation) set = common words only. Validation (answer) set additionally
            // contains an obscure word "quartz" a player could still bank. Nobody banks anything.
            var boardSet = Findable("cat", "table");
            var validationSet = Findable("cat", "table", "quartz");
            var round = ScoreRound(1, Default, ("Alice", Guid.NewGuid(), System.Array.Empty<string>()));

            var reveal = RevealBuilder.Build(validationSet, boardSet, round, Default);

            // "words nobody found" is drawn from the board set, so the obscure "quartz" never
            // clutters it even though it was findable under the answer dictionary.
            var nobody = reveal.WordsNobodyFound.Select(m => m.Word).ToArray();
            CollectionAssert.AreEquivalent(new[] { "cat", "table" }, nobody);
            CollectionAssert.DoesNotContain(nobody, "quartz");

            // The theoretical maximum is computed from the validation set, so it includes
            // "quartz" — a player who finds it can reach the max rather than exceed it.
            int expectedMax = validationSet.Keys.Sum(w => TraceryScorer.WordScore(w, isUnique: true, Default));
            Assert.AreEqual(expectedMax, reveal.TheoreticalMax);
            int boardOnlyMax = boardSet.Keys.Sum(w => TraceryScorer.WordScore(w, isUnique: true, Default));
            Assert.IsTrue(reveal.TheoreticalMax > boardOnlyMax,
                "Theoretical max must reflect the wider answer dictionary, not just the board words.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        // A findable set keyed by word; each carries a simple ascending cell path so PathFor has
        // something to surface. Path values don't affect any beat other than the carried path.
        private static IReadOnlyDictionary<string, TracedWord> Findable(params string[] words)
            => words.ToDictionary(
                w => w,
                w => new TracedWord(w, Enumerable.Range(0, w.Length).ToArray()),
                StringComparer.Ordinal);

        // Scores scripted banks into a RoundResult the way the engine does: unique-find is resolved
        // across every player's banks, each word scored via TraceryScorer. Cumulative = this round's
        // points (single-round scenarios), which is all the beats under test read.
        private static RoundResult ScoreRound(
            int round, TracerySettings settings,
            params (string Name, Guid Id, string[] Words)[] players)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var p in players)
                foreach (var w in p.Words.Distinct())
                    counts[w] = counts.GetValueOrDefault(w) + 1;

            var outcomes = players.Select(p =>
            {
                var scores = p.Words.Distinct()
                    .Select(w => TraceryScorer.Score(w, counts[w] == 1, settings))
                    .ToImmutableArray();
                int total = scores.Sum(s => s.Points);
                return new TraceryPlayerRoundOutcome
                {
                    UserId = p.Id,
                    DisplayName = p.Name,
                    PointsAwarded = total,
                    CumulativeScore = total,
                    WordScores = scores
                };
            }).ToImmutableArray();

            return new RoundResult { RoundNumber = round, Outcomes = outcomes };
        }

        private static TraceryPlayerRoundOutcome Outcome(Guid id, string name, int points, int cumulative)
            => new()
            {
                UserId = id,
                DisplayName = name,
                PointsAwarded = points,
                CumulativeScore = cumulative,
                WordScores = []
            };
    }
}
