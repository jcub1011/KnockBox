using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    public static class TokenVisibilityFilter
    {
        public static IEnumerable<Token> VisibleTokensFor(IEnumerable<Token> tokens, bool isHost)
        {
            ArgumentNullException.ThrowIfNull(tokens);
            return isHost ? tokens : tokens.Where(t => !t.Hidden);
        }

        /// <summary>
        /// Three-arg overload that also drops non-host tokens sitting on a fogged
        /// cell. A token at (X, Y) is in cell (floor(X), floor(Y)).
        /// </summary>
        public static IEnumerable<Token> VisibleTokensFor(IEnumerable<Token> tokens, Map map, bool isHost)
        {
            ArgumentNullException.ThrowIfNull(tokens);
            ArgumentNullException.ThrowIfNull(map);
            if (isHost) return tokens;
            return tokens.Where(t =>
            {
                if (t.Hidden) return false;
                var cx = (int)Math.Floor(t.X);
                var cy = (int)Math.Floor(t.Y);
                return !map.IsFogged(cx, cy);
            });
        }
    }
}
