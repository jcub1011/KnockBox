using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic;
using KnockBox.Tracery.Services.Logic.Dictionary;

namespace KnockBox.Tracery.Tests.Unit.Logic
{
    /// <summary>
    /// The mechanism the engine relies on to let a host generate boards from a common-word
    /// (board) dictionary while validating answers against a wider one: two solvers over two
    /// tries. A word present only in the answer dictionary must be absent from the board
    /// (generation) solve yet still validate — so players can bank obscure finds on a board
    /// built from common words.
    /// </summary>
    [TestClass]
    public class DictionarySplitTests
    {
        // Same fixed 3×3 board as the solver tests:
        //   T(0) A(1) B(2)
        //   R(3) C(4) E(5)
        //   P(6) O(7) D(8)
        private static Grid MakeBoard() => new(3, 3, "tabrcepod");

        // "trace" (T0→R3→A1→C4→E5) is the answer-only word; "cab"/"car" are the common words.
        private static TracerySolver BoardSolver() => new(TraceryTrie.FromWords("cab", "car"));
        private static TracerySolver AnswerSolver() => new(TraceryTrie.FromWords("cab", "car", "trace"));

        [TestMethod]
        public void BoardSolve_ExcludesAnswerOnlyWord_ButAnswerSolveIncludesIt()
        {
            var grid = MakeBoard();

            var boardFindable = BoardSolver().Solve(grid, minWordLength: 3);
            var answerFindable = AnswerSolver().Solve(grid, minWordLength: 3);

            CollectionAssert.AreEquivalent(new[] { "cab", "car" }, boardFindable.Keys.ToArray());
            CollectionAssert.AreEquivalent(new[] { "cab", "car", "trace" }, answerFindable.Keys.ToArray());

            // The board (common-word) set is a subset of the answer set.
            Assert.IsTrue(boardFindable.Keys.All(answerFindable.ContainsKey));
        }

        [TestMethod]
        public void AnswerOnlyWord_Validates_OnlyAgainstTheAnswerDictionary()
        {
            var grid = MakeBoard();
            int[] tracePath = [0, 3, 1, 4, 5]; // T-R-A-C-E

            // Validates against the wider answer dictionary…
            var accepted = AnswerSolver().ValidateTrace(grid, tracePath, minWordLength: 3);
            Assert.IsTrue(accepted.TryGetSuccess(out var word));
            Assert.AreEqual("trace", word);

            // …but the same trace is rejected by the board (common-word) dictionary.
            var rejected = BoardSolver().ValidateTrace(grid, tracePath, minWordLength: 3);
            Assert.IsFalse(rejected.TryGetSuccess(out _));
        }
    }
}
