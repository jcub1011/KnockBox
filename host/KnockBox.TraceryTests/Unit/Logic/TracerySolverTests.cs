using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Services.Logic.Dictionary;

namespace KnockBox.Tracery.Tests.Unit.Logic
{
    [TestClass]
    public class TracerySolverTests
    {
        // A fixed 3×3 board used by most tests. Cell ids are row-major:
        //   T(0) A(1) B(2)
        //   R(3) C(4) E(5)
        //   P(6) O(7) D(8)
        // All nine letters are distinct, so any repeated letter in a word forces a
        // revisit — handy for the self-intersection case.
        private static Grid MakeBoard() => new(3, 3, "tabrcepod");

        // ── Solve: exact findable set ───────────────────────────────────────────

        [TestMethod]
        public void Solve_ReturnsExactlyTheFindableDictionaryWords()
        {
            var grid = MakeBoard();
            // Mix of findable words (various path shapes) and unfindable ones.
            var trie = TraceryTrie.FromWords(
                "trace",  // 5, bending: T0→R3→A1→C4→E5
                "cab",    // C4→A1→B2
                "car",    // C4→A1→R3  (diagonal A→R)
                "ace",    // A1→C4→E5
                "cod",    // C4→O7→D8
                "bat",    // B2→A1→T0  (straight)
                "bar",    // B2→A1→R3
                "ear",    // E5→A1→R3  (diagonal-only: both steps diagonal)
                "are",    // present letters, but R3 and E5 aren't adjacent → unfindable
                "bad",    // A1 and D8 aren't adjacent → unfindable
                "tat");   // only one T, so the second T would revisit cell 0 → unfindable

            var solver = new TracerySolver(trie);
            var found = solver.Solve(grid, minWordLength: 3);

            var expected = new[] { "trace", "cab", "car", "ace", "cod", "bat", "bar", "ear" };
            CollectionAssert.AreEquivalent(expected, found.Keys.ToArray());
        }

        [TestMethod]
        public void Solve_FindsBendingPath_WithCorrectCellTrace()
        {
            var grid = MakeBoard();
            var solver = new TracerySolver(TraceryTrie.FromWords("trace"));

            var found = solver.Solve(grid, minWordLength: 3);

            Assert.IsTrue(found.TryGetValue("trace", out var traced));
            CollectionAssert.AreEqual(new[] { 0, 3, 1, 4, 5 }, traced!.Path.ToArray());
        }

        [TestMethod]
        public void Solve_FindsDiagonalOnlyWord()
        {
            var grid = MakeBoard();
            var solver = new TracerySolver(TraceryTrie.FromWords("ear"));

            var found = solver.Solve(grid, minWordLength: 3);

            // E5→A1→R3 — every step changes both row and column.
            Assert.IsTrue(found.TryGetValue("ear", out var traced));
            CollectionAssert.AreEqual(new[] { 5, 1, 3 }, traced!.Path.ToArray());
        }

        [TestMethod]
        public void Solve_ExcludesSelfIntersectingWord()
        {
            var grid = MakeBoard();
            // "tat" needs two Ts, but the board has one; the only spelling revisits cell 0.
            var solver = new TracerySolver(TraceryTrie.FromWords("tat"));

            var found = solver.Solve(grid, minWordLength: 3);

            Assert.AreEqual(0, found.Count);
        }

        [TestMethod]
        public void Solve_ReusesCellsAcrossDifferentWords()
        {
            var grid = MakeBoard();
            // "cab" and "ace" both pass through C4 and A1 — a fresh visited set per word
            // means a cell consumed by one word is free for the next.
            var solver = new TracerySolver(TraceryTrie.FromWords("cab", "ace"));

            var found = solver.Solve(grid, minWordLength: 3);

            Assert.IsTrue(found.ContainsKey("cab"));
            Assert.IsTrue(found.ContainsKey("ace"));
            CollectionAssert.Contains(found["cab"].Path.ToArray(), 4); // C4
            CollectionAssert.Contains(found["ace"].Path.ToArray(), 4); // C4 again
        }

        [TestMethod]
        public void Solve_OmitsWordsBelowMinWordLength()
        {
            var grid = MakeBoard();
            var trie = TraceryTrie.FromWords("trace", "cab", "car", "ace", "ear");

            var solver = new TracerySolver(trie);
            var found = solver.Solve(grid, minWordLength: 4);

            // Only "trace" clears the length-4 floor; the 3-letter words drop out.
            CollectionAssert.AreEquivalent(new[] { "trace" }, found.Keys.ToArray());
        }

        // ── Solve: prefix pruning ───────────────────────────────────────────────

        [TestMethod]
        public void Solve_PrunesDeadPrefixes_VisitsEachCellOnceWhenNothingMatches()
        {
            // An empty dictionary means no single letter is even a prefix, so the DFS
            // must abandon every branch at depth 1. With pruning that's exactly one
            // visit per starting cell; without it the solver would walk every path.
            var grid = new Grid(5, 5, "abcdefghijklmnopqrstuvwxy");
            var solver = new TracerySolver(TraceryTrie.FromWords());

            var found = solver.Solve(grid, minWordLength: 3);

            Assert.AreEqual(0, found.Count);
            Assert.AreEqual(grid.CellCount, solver.LastSolveVisitedCells);
        }

        // ── ValidateTrace ───────────────────────────────────────────────────────

        private static TracerySolver ValidatingSolver()
            => new(TraceryTrie.FromWords("trace"));

        [TestMethod]
        public void ValidateTrace_AcceptsLegalTrace_ReturnsWord()
        {
            var grid = MakeBoard();
            var result = ValidatingSolver().ValidateTrace(grid, new[] { 0, 3, 1, 4, 5 }, minWordLength: 4);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("trace", result.Value);
        }

        [TestMethod]
        public void ValidateTrace_RejectsTooShortTrace()
        {
            var grid = MakeBoard();
            var result = ValidatingSolver().ValidateTrace(grid, new[] { 0, 3 }, minWordLength: 4);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void ValidateTrace_RejectsNonAdjacentJump()
        {
            var grid = MakeBoard();
            // R3 (1,0) → D8 (2,2) is not 8-way adjacent.
            var result = ValidatingSolver().ValidateTrace(grid, new[] { 0, 3, 8 }, minWordLength: 3);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void ValidateTrace_RejectsRevisitedCell()
        {
            var grid = MakeBoard();
            var result = ValidatingSolver().ValidateTrace(grid, new[] { 0, 3, 0 }, minWordLength: 3);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void ValidateTrace_RejectsOutOfRangeCell()
        {
            var grid = MakeBoard();
            var result = ValidatingSolver().ValidateTrace(grid, new[] { 0, 1, 99 }, minWordLength: 3);

            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public void ValidateTrace_RejectsNonDictionaryWord()
        {
            var grid = MakeBoard();
            // T0→A1→B2 spells "tab" — a legal path, but not in this trie.
            var result = ValidatingSolver().ValidateTrace(grid, new[] { 0, 1, 2 }, minWordLength: 3);

            Assert.IsTrue(result.IsFailure);
        }
    }
}
