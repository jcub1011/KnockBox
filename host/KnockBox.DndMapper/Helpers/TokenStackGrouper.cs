using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    public readonly record struct TokenCellKey(int CellX, int CellY);

    public sealed class TokenStack
    {
        public TokenCellKey Cell { get; init; }
        public List<Token> Tokens { get; init; } = [];
    }

    public static class TokenStackGrouper
    {
        // Tokens are stored at cell centers (e.g. 0.5, 1.5). Map a token's X/Y
        // to the integer cell it occupies. Tokens at non-snapped positions still
        // group with whichever cell their floor lands in.
        public static TokenCellKey CellOf(double x, double y)
            => new((int)Math.Floor(x), (int)Math.Floor(y));

        public static List<TokenStack> Group(IEnumerable<Token> tokens)
        {
            var map = new Dictionary<TokenCellKey, TokenStack>();
            foreach (var t in tokens)
            {
                var key = CellOf(t.X, t.Y);
                if (!map.TryGetValue(key, out var stack))
                {
                    stack = new TokenStack { Cell = key };
                    map[key] = stack;
                }
                stack.Tokens.Add(t);
            }
            // Preserve a deterministic ordering: rows then columns.
            return [.. map.Values.OrderBy(s => s.Cell.CellY).ThenBy(s => s.Cell.CellX)];
        }
    }
}
