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
    }
}
