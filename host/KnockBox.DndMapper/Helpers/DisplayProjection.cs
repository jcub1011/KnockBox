using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    // Centralizes "what the display sees" so the public read-only display view
    // page never iterates state directly. Hidden tokens and hidden images are
    // dropped here, the roll log honors RollsVisibleToPlayers, and images come
    // back ordered by LayerOrder so the razor can iterate without re-sorting.
    public sealed record DisplayProjection(
        Map? ActiveMap,
        IReadOnlyList<MapImage> VisibleImages,
        IReadOnlyList<Token> VisibleTokens,
        string? MarkupSvg,
        CombatState? ActiveCombat,
        IReadOnlyList<RollResult> VisibleRollLog)
    {
        public static DisplayProjection Build(DndMapperGameState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            var map = state.ActiveMapId is { } id
                ? state.Maps.FirstOrDefault(m => m.Id == id)
                : null;

            if (map is null)
                return new DisplayProjection(null, [], [], null, state.ActiveCombat, []);

            var images = map.Images
                .Where(i => !i.Hidden)
                .OrderBy(i => i.LayerOrder)
                .ToArray();

            var tokens = map.Tokens
                .Where(t => !t.Hidden)
                .ToArray();

            var rolls = state.Settings.RollsVisibleToPlayers
                ? state.RollLog.TakeLast(10).Reverse().ToArray()
                : Array.Empty<RollResult>();

            return new DisplayProjection(map, images, tokens, map.MarkupSvg, state.ActiveCombat, rolls);
        }
    }
}
