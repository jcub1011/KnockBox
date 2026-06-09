using System.Text.Json.Serialization;

namespace KnockBox.Tracery.Models
{
    /// <summary>
    /// An immutable W×H letter grid with precomputed 8-way (orthogonal + diagonal)
    /// adjacency. Cells are addressed either by <c>(row, col)</c> or by a row-major
    /// <em>cell id</em> (<c>row * Width + col</c>); the solver works exclusively in
    /// cell ids, so the adjacency table is keyed by id. The table is built once at
    /// construction because the solver's DFS reads neighbours on every step.
    /// </summary>
    /// <remarks>
    /// Lives in the Contracts assembly because the WASM client needs the adjacency
    /// logic for the live trace preview. It is JSON-round-trippable on the wire via the
    /// <see cref="Letters"/> property + the <c>[JsonConstructor]</c> string ctor below;
    /// the private adjacency table is rebuilt on deserialization (never serialized).
    /// </remarks>
    public sealed class Grid
    {
        private readonly char[] _letters; // row-major
        private readonly int[][] _neighbors; // adjacency table, indexed by cell id

        public int Width { get; }
        public int Height { get; }

        /// <summary>The grid's letters in row-major order — the only state carried on the wire.</summary>
        public string Letters => new(_letters);

        /// <summary>Total number of cells (<c>Width * Height</c>).</summary>
        [JsonIgnore]
        public int CellCount => _letters.Length;

        /// <summary>
        /// JSON deserialization ctor (and the WASM client's path back from a projected
        /// <see cref="Letters"/> string). Delegates to the span ctor, which rebuilds the
        /// adjacency table — so a deserialized grid is fully functional for the trace preview.
        /// </summary>
        [JsonConstructor]
        public Grid(int width, int height, string letters)
            : this(width, height, letters.AsSpan())
        {
        }

        /// <summary>
        /// Builds a grid from a row-major letter array. <paramref name="letters"/> must
        /// contain exactly <c>width * height</c> entries; the array is copied so later
        /// mutation of the caller's array does not affect the grid.
        /// </summary>
        public Grid(int width, int height, ReadOnlySpan<char> letters)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (letters.Length != width * height)
                throw new ArgumentException(
                    $"Expected {width * height} letters for a {width}×{height} grid but got {letters.Length}.",
                    nameof(letters));

            Width = width;
            Height = height;
            _letters = letters.ToArray();
            _neighbors = BuildAdjacency(width, height);
        }

        /// <summary>The letter at the given cell id.</summary>
        public char this[int cellId] => _letters[cellId];

        /// <summary>The letter at the given <c>(row, col)</c>.</summary>
        public char this[int r, int c] => _letters[CellId(r, c)];

        public int CellId(int r, int c) => r * Width + c;

        public (int r, int c) FromCellId(int cellId) => (cellId / Width, cellId % Width);

        /// <summary>The 8-way neighbours of <paramref name="cellId"/> (edge/corner aware).</summary>
        public IReadOnlyList<int> Neighbors(int cellId) => _neighbors[cellId];

        /// <summary>
        /// True if the two cells are 8-way adjacent (touching, distinct). The single
        /// source of truth for "is this a legal trace step" — see
        /// <c>TracerySolver.ValidateTrace</c>.
        /// </summary>
        public bool AreAdjacent(int a, int b)
        {
            if (a == b) return false;
            var (ar, ac) = FromCellId(a);
            var (br, bc) = FromCellId(b);
            return Math.Abs(ar - br) <= 1 && Math.Abs(ac - bc) <= 1;
        }

        private static int[][] BuildAdjacency(int width, int height)
        {
            var table = new int[width * height][];
            var buffer = new List<int>(8);
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    buffer.Clear();
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            int nr = r + dr, nc = c + dc;
                            if (nr < 0 || nr >= height || nc < 0 || nc >= width) continue;
                            buffer.Add(nr * width + nc);
                        }
                    }
                    table[r * width + c] = buffer.ToArray();
                }
            }
            return table;
        }
    }
}
