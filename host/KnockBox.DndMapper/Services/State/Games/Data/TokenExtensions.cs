using System.Collections.Generic;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public static class TokenExtensions
    {
        // Sheet color overrides token color when the token is linked to a
        // sheet that carries a non-empty color. This is how "all tokens for the
        // same character look the same" — every render site routes through here
        // instead of reading Token.Color directly. When the sheet has no color
        // (legacy saves) or no sheet is linked, the token's own color wins.
        public static string ResolveColor(this Token token, IReadOnlyDictionary<System.Guid, CharacterSheet> sheets)
        {
            if (token.SheetId is System.Guid sid
                && sheets.TryGetValue(sid, out var sheet)
                && !string.IsNullOrEmpty(sheet.Color))
            {
                return sheet.Color;
            }
            return token.Color;
        }
    }
}
