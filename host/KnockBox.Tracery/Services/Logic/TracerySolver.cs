using KnockBox.Core.Primitives.Returns;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic.Dictionary;

namespace KnockBox.Tracery.Services.Logic
{
    /// <summary>
    /// The single grid solver (GDD §9). Pure logic over a <see cref="Grid"/> and a
    /// <see cref="TraceryTrie"/> — no Blazor or game-state dependencies — so it is
    /// exhaustively unit-testable. It serves two jobs:
    /// <list type="bullet">
    /// <item><see cref="Solve"/> — the complete set of findable words (board generation,
    /// scoring input, reveal data).</item>
    /// <item><see cref="ValidateTrace"/> — the authoritative runtime check for a player's
    /// submitted path. The UI must route every banked word through here rather than
    /// re-implementing adjacency or dictionary rules.</item>
    /// </list>
    /// </summary>
    public sealed class TracerySolver
    {
        private readonly TraceryTrie _trie;

        // Test instrumentation: the number of DFS cell-visits made by the most recent
        // Solve call. With prefix pruning a hostile board collapses to one visit per
        // starting cell; tests assert against that to prove dead prefixes aren't explored.
        internal long LastSolveVisitedCells { get; private set; }

        internal TracerySolver(TraceryTrie trie) => _trie = trie;

        /// <summary>
        /// Every distinct word findable on <paramref name="grid"/> by an 8-way trace of
        /// length ≥ <paramref name="minWordLength"/> with no self-intersection. Keyed by
        /// the word; the value carries one representative path (the first found — paths
        /// only feed the reveal animation, so which one is kept doesn't affect scoring).
        /// A DFS runs from every cell with a fresh visited set, so cells are freely
        /// reused across different words. Prefix pruning abandons a branch the moment its
        /// accumulated string stops being any word's prefix.
        /// </summary>
        public IReadOnlyDictionary<string, TracedWord> Solve(Grid grid, int minWordLength)
        {
            ArgumentNullException.ThrowIfNull(grid);
            LastSolveVisitedCells = 0;

            var results = new Dictionary<string, TracedWord>();
            int n = grid.CellCount;
            var onPath = new bool[n];
            var pathCells = new int[n];
            var chars = new char[n];

            for (int start = 0; start < n; start++)
                Dfs(grid, start, 0, TraceryTrie.Root, onPath, pathCells, chars, results, minWordLength);

            return results;
        }

        private void Dfs(
            Grid grid, int cell, int depth, int node,
            bool[] onPath, int[] pathCells, char[] chars,
            Dictionary<string, TracedWord> results, int minWordLength)
        {
            LastSolveVisitedCells++;

            // One transition from the node reached by the path so far, instead of re-walking
            // the whole accumulated string from the root every step. A negative result means
            // nothing extends this prefix, so neither this cell nor any descendant can be a
            // word — bail before recursing. This is the pruning the solver hinges on.
            int child = _trie.Transition(node, grid[cell]);
            if (child < 0)
                return;

            onPath[cell] = true;
            pathCells[depth] = cell;
            chars[depth] = grid[cell];
            int len = depth + 1;

            // The reached node's own end-of-word flag answers IsWord directly — no second walk.
            if (len >= minWordLength && _trie.IsWordNode(child))
            {
                var word = chars.AsSpan(0, len);
                string key = new(word);
                if (!results.ContainsKey(key))
                    results[key] = new TracedWord(key, pathCells.AsSpan(0, len).ToArray());
            }

            foreach (int next in grid.Neighbors(cell))
            {
                if (!onPath[next])
                    Dfs(grid, next, depth + 1, child, onPath, pathCells, chars, results, minWordLength);
            }

            onPath[cell] = false;
        }

        /// <summary>
        /// Validates a player's submitted trace against the same rules the solver
        /// enforces, returning the spelled word on success. Rejects (in order): a path
        /// shorter than <paramref name="minWordLength"/>; an out-of-range cell; a repeated
        /// cell; a non-adjacent jump; and a spelled string that isn't a dictionary word.
        /// </summary>
        public ValueResult<string> ValidateTrace(Grid grid, IReadOnlyList<int> path, int minWordLength)
        {
            ArgumentNullException.ThrowIfNull(grid);

            int count = path?.Count ?? 0;
            if (count < minWordLength)
                return ValueResult<string>.FromError(
                    "That trace is too short.",
                    $"Trace length {count} is below the minimum word length {minWordLength}.");

            var seen = new HashSet<int>(count);
            var chars = new char[count];
            for (int i = 0; i < count; i++)
            {
                int cell = path![i];
                if (cell < 0 || cell >= grid.CellCount)
                    return ValueResult<string>.FromError(
                        "That trace left the grid.",
                        $"Cell id {cell} is outside [0, {grid.CellCount}).");

                if (!seen.Add(cell))
                    return ValueResult<string>.FromError(
                        "A tile can't be used twice in one word.",
                        $"Cell id {cell} appears more than once in the trace.");

                if (i > 0 && !grid.AreAdjacent(path[i - 1], cell))
                    return ValueResult<string>.FromError(
                        "Traces have to move between touching tiles.",
                        $"Cell ids {path[i - 1]} and {cell} are not 8-way adjacent.");

                chars[i] = grid[cell];
            }

            var word = chars.AsSpan();
            if (!_trie.IsWord(word))
                return ValueResult<string>.FromError(
                    "That isn't a word.",
                    $"\"{new string(word)}\" is not in the dictionary.");

            return new string(word);
        }
    }
}
