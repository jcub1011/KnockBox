using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    // Centralizes "what the display sees" so the public read-only display view
    // page never iterates state directly. Hidden tokens / hidden images are
    // dropped; tokens on fogged cells are filtered out; images fully covered by
    // fog are dropped (lean-toward-showing rule). The fog mask itself is
    // enumerated into FoggedCells once per Build so the razor doesn't loop the
    // bitset on every re-render.
    public sealed record DisplayProjection(
        Map? ActiveMap,
        IReadOnlyList<MapImage> VisibleImages,
        IReadOnlyList<Token> VisibleTokens,
        string? MarkupSvg,
        CombatState? ActiveCombat,
        IReadOnlyList<RollResult> VisibleRollLog,
        IReadOnlyList<(int cx, int cy)> FoggedCells)
    {
        public static DisplayProjection Build(DndMapperGameState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            var map = state.ActiveMapId is { } id
                ? state.Maps.FirstOrDefault(m => m.Id == id)
                : null;

            if (map is null)
                return new DisplayProjection(null, [], [], null, state.ActiveCombat, [], []);

            var images = ImageVisibilityFilter.VisibleImagesFor(map.Images, map, isHost: false)
                .OrderBy(i => i.LayerOrder)
                .ToArray();

            var tokens = TokenVisibilityFilter.VisibleTokensFor(map.Tokens, map, isHost: false)
                .ToArray();

            var rolls = state.Settings.RollsVisibleToPlayers
                ? state.RollLog.TakeLast(10).Reverse().ToArray()
                : Array.Empty<RollResult>();

            var fogCells = CollectFoggedCells(map);

            return new DisplayProjection(map, images, tokens, map.MarkupSvg, state.ActiveCombat, rolls, fogCells);
        }

        private static IReadOnlyList<(int cx, int cy)> CollectFoggedCells(Map map)
        {
            if (map.FogMask.Length == 0) return Array.Empty<(int, int)>();
            var cells = new List<(int cx, int cy)>();
            for (var cy = 0; cy < map.Grid.HeightCells; cy++)
                for (var cx = 0; cx < map.Grid.WidthCells; cx++)
                    if (map.IsFogged(cx, cy))
                        cells.Add((cx, cy));
            return cells;
        }
    }
}
