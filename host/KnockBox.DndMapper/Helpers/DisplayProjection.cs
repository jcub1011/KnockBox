using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    // Centralizes "what the display sees" so the public read-only display view
    // page never iterates state directly. Hidden tokens / hidden images are
    // dropped; tokens on fogged cells are filtered out; images fully covered by
    // fog are dropped (lean-toward-showing rule). FogPathData is the SVG path
    // string covering every fogged cluster as a single polygon (with holes
    // resolved via fill-rule="evenodd") and is recomputed once per Build.
    public sealed record DisplayProjection(
        Map? ActiveMap,
        IReadOnlyList<MapImage> VisibleImages,
        IReadOnlyList<Token> VisibleTokens,
        string? MarkupSvg,
        CombatState? ActiveCombat,
        IReadOnlyList<RollResult> VisibleRollLog,
        string FogPathData)
    {
        public static DisplayProjection Build(DndMapperGameState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            var map = state.ActiveMapId is { } id
                ? state.Maps.FirstOrDefault(m => m.Id == id)
                : null;

            if (map is null)
                return new DisplayProjection(null, [], [], null, state.ActiveCombat, [], string.Empty);

            var images = ImageVisibilityFilter.VisibleImagesFor(map.Images, map, isHost: false)
                .OrderBy(i => i.LayerOrder)
                .ToArray();

            var tokens = TokenVisibilityFilter.VisibleTokensFor(map.Tokens, map, isHost: false)
                .ToArray();

            var rolls = state.Settings.RollsVisibleToPlayers
                ? state.RollLog.TakeLast(10).Reverse().ToArray()
                : Array.Empty<RollResult>();

            var fogPath = FogPolygonBuilder.BuildSvgPathData(map);

            return new DisplayProjection(map, images, tokens, map.MarkupSvg, state.ActiveCombat, rolls, fogPath);
        }
    }
}
