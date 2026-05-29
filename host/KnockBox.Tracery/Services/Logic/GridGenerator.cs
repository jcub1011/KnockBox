using System.Text;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Tracery.Models;
using KnockBox.WordService.Contracts;
using Microsoft.Extensions.Logging;

namespace KnockBox.Tracery.Services.Logic
{
    /// <summary>
    /// A board accepted by the generator, paired with the findable-word set the solver
    /// already computed while checking the quality bar. Both are returned so the round
    /// loop (M04) never has to re-solve the same grid. <see cref="UsedFallback"/> records
    /// whether the seed fallback fired, for tuning telemetry and tests.
    /// </summary>
    internal sealed record GeneratedBoard(
        Grid Grid,
        IReadOnlyDictionary<string, TracedWord> FindableWords,
        bool UsedFallback);

    /// <summary>
    /// Produces boards that are reliably word-rich with at least one big find (GDD §6):
    /// sample letters from <see cref="LetterDistribution"/>, solve the candidate, and accept
    /// only boards clearing a tunable quality bar. If the bar can't be met within the attempt
    /// cap, a seed fallback plants a known dictionary word along a legal path so no round is
    /// ever dead. Pure logic over the solver — no Blazor or game-state coupling.
    /// </summary>
    internal sealed class GridGenerator
    {
        private readonly TracerySolver _solver;
        private readonly IRandomNumberService _rng;
        private readonly IWordListService _wordList;
        private readonly ILogger _logger;

        // Engine defaults applied when the matching TracerySettings knob is left at 0.
        private const int DefaultMaxAttempts = 50;
        private const double FindableWordsPerCell = 0.75; // scales the findable-count floor with area
        private const int MinFindableFloor = 8;           // ...but never demand fewer than this

        internal GridGenerator(
            TracerySolver solver,
            IRandomNumberService rng,
            IWordListService wordList,
            ILogger logger)
        {
            _solver = solver;
            _rng = rng;
            _wordList = wordList;
            _logger = logger;
        }

        /// <summary>
        /// Generates an accepted board for the given settings, falling back to a planted
        /// seed word if generate-and-test exhausts its attempts. Fails only when the grid
        /// physically can't host a legal word (<c>MinWordLength &gt; CellCount</c>) or no
        /// plantable dictionary word exists for the fallback.
        /// </summary>
        internal ValueResult<GeneratedBoard> Generate(TracerySettings settings)
        {
            int w = settings.GridWidth, h = settings.GridHeight, area = w * h;
            if (w <= 0 || h <= 0)
                return ValueResult<GeneratedBoard>.FromError(
                    "Failed to generate a board.", $"Invalid grid dimensions {w}x{h}.");

            int minWordLength = settings.MinWordLength;
            if (minWordLength > area)
                return ValueResult<GeneratedBoard>.FromError(
                    "Failed to generate a board.",
                    $"Minimum word length {minWordLength} exceeds the {w}x{h} grid's {area} cells, so no legal word fits.");

            int effectiveMinFindable = settings.MinFindableWords > 0
                ? settings.MinFindableWords
                : Math.Max(MinFindableFloor, (int)Math.Round(area * FindableWordsPerCell));
            int maxAttempts = settings.MaxGenerationAttempts > 0
                ? settings.MaxGenerationAttempts
                : DefaultMaxAttempts;

            // Reuse one buffer across attempts; stackalloc for the realistic grid sizes.
            Span<char> letters = area <= 256 ? stackalloc char[area] : new char[area];

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                for (int i = 0; i < area; i++)
                    letters[i] = LetterDistribution.Next(_rng);

                var grid = new Grid(w, h, letters);
                var findable = _solver.Solve(grid, minWordLength);

                if (ClearsBar(grid, findable, effectiveMinFindable, settings.MinLongWordLength, settings.RequireRareLetterWord))
                    return new GeneratedBoard(grid, findable, UsedFallback: false);
            }

            _logger.LogInformation(
                "Tracery grid generation exhausted {Attempts} attempts at {Width}x{Height}; using seed fallback.",
                maxAttempts, w, h);
            return Fallback(settings, minWordLength, area);
        }

        /// <summary>
        /// True if the solved board clears the full quality bar: enough findable words, at
        /// least one "big find" (length ≥ <paramref name="minLongWordLength"/>, clamped to
        /// what the grid can physically hold), and — when required — at least one word using
        /// a rare-letter tile.
        /// </summary>
        private static bool ClearsBar(
            Grid grid,
            IReadOnlyDictionary<string, TracedWord> findable,
            int effectiveMinFindable,
            int minLongWordLength,
            bool requireRareLetterWord)
        {
            if (findable.Count < effectiveMinFindable)
                return false;

            // A path can't be longer than the cell count, so a bar above that is unmeetable
            // by any board; clamp it to "the longest word the grid can hold" instead.
            int longTarget = Math.Min(minLongWordLength, grid.CellCount);
            bool hasLong = findable.Keys.Any(word => word.Length >= longTarget);
            if (!hasLong)
                return false;

            if (requireRareLetterWord)
            {
                bool hasRare = findable.Keys.Any(word => word.Any(LetterDistribution.IsRare));
                if (!hasRare)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Seed fallback (GDD §6): lay a real dictionary word along a legal self-avoiding
        /// 8-way path, fill the rest from the distribution, and re-solve. Guarantees the
        /// big find; returned unconditionally (degrades gracefully if an inflated findable
        /// count still isn't met — nothing better can be done for that grid).
        /// </summary>
        private ValueResult<GeneratedBoard> Fallback(TracerySettings settings, int minWordLength, int area)
        {
            int w = settings.GridWidth, h = settings.GridHeight;

            // Plant a word that both satisfies the (clamped) big-find bar and fits as a path.
            int plantLen = Math.Clamp(settings.MinLongWordLength, minWordLength, area);

            if (!TryPickPlantWord(plantLen, minWordLength, out string word, out int wordLen))
                return ValueResult<GeneratedBoard>.FromError(
                    "Failed to generate a board.",
                    $"No dictionary word of length [{minWordLength}, {plantLen}] was available to seed the {w}x{h} grid.");

            // Fill every cell first so the scratch grid's adjacency table is available for the
            // path search; then overwrite the path cells with the planted word's letters.
            char[] letters = new char[area];
            for (int i = 0; i < area; i++)
                letters[i] = LetterDistribution.Next(_rng);

            var scratch = new Grid(w, h, letters);
            int[]? path = FindSelfAvoidingPath(scratch, wordLen);
            if (path is null)
                return ValueResult<GeneratedBoard>.FromError(
                    "Failed to generate a board.",
                    $"Could not route a length-{wordLen} path on the {w}x{h} grid for the seed word.");

            for (int k = 0; k < wordLen; k++)
                letters[path[k]] = word[k];

            var grid = new Grid(w, h, letters);
            var findable = _solver.Solve(grid, minWordLength);
            return new GeneratedBoard(grid, findable, UsedFallback: true);
        }

        /// <summary>
        /// Picks a random full-dictionary word of <paramref name="preferredLength"/>, walking
        /// the length down to <paramref name="minWordLength"/> if a length has no entries (only
        /// happens with tiny test dictionaries — the real one is dense at every length).
        /// </summary>
        private bool TryPickPlantWord(int preferredLength, int minWordLength, out string word, out int length)
        {
            for (int len = preferredLength; len >= minWordLength; len--)
            {
                int count = _wordList.GetWordCount(WordPoolMode.FullDictionary, len);
                if (count <= 0) continue;

                int index = _rng.GetRandomInt(count);
                word = Encoding.ASCII.GetString(_wordList.GetWord(WordPoolMode.FullDictionary, len, index));
                length = len;
                return true;
            }

            word = string.Empty;
            length = 0;
            return false;
        }

        /// <summary>
        /// Finds a self-avoiding 8-way path of <paramref name="length"/> cells via randomized
        /// DFS (rng-shuffled neighbours, like Spardle's Fisher–Yates), retrying from different
        /// random starts on a dead-end. On a fully connected grid a simple path of length
        /// ≤ CellCount always exists, so this only fails when length &gt; CellCount.
        /// </summary>
        private int[]? FindSelfAvoidingPath(Grid grid, int length)
        {
            int area = grid.CellCount;
            if (length > area) return null;

            var visited = new bool[area];
            var path = new int[length];

            for (int start = 0; start < area; start++)
            {
                Array.Clear(visited);
                int first = _rng.GetRandomInt(area);
                path[0] = first;
                visited[first] = true;
                if (Walk(grid, path, visited, depth: 1, length))
                    return path;
            }

            return null;
        }

        private bool Walk(Grid grid, int[] path, bool[] visited, int depth, int length)
        {
            if (depth == length) return true;

            int[] order = grid.Neighbors(path[depth - 1]).ToArray();
            Shuffle(order);

            foreach (int next in order)
            {
                if (visited[next]) continue;
                visited[next] = true;
                path[depth] = next;
                if (Walk(grid, path, visited, depth + 1, length))
                    return true;
                visited[next] = false;
            }

            return false;
        }

        private void Shuffle(int[] items)
        {
            for (int n = items.Length - 1; n > 0; n--)
            {
                int k = _rng.GetRandomInt(n + 1);
                (items[n], items[k]) = (items[k], items[n]);
            }
        }
    }
}
