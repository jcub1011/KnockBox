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
        string FogPathData,
        FocusRect? FocusRect,
        Guid? ActiveTurnTokenId)
    {
        public static DisplayProjection Build(DndMapperGameState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            var map = state.ActiveMapId is { } id
                ? state.Maps.FirstOrDefault(m => m.Id == id)
                : null;

            var activeTokenId = ResolveActiveTurnTokenId(state.ActiveCombat);

            if (map is null)
                return new DisplayProjection(null, [], [], null, state.ActiveCombat, [], string.Empty, null, activeTokenId);

            var images = ImageVisibilityFilter.VisibleImagesFor(map.Images, map, isHost: false)
                .OrderBy(i => i.LayerOrder)
                .ToArray();

            var tokens = TokenVisibilityFilter.VisibleTokensFor(map.Tokens, map, isHost: false)
                .ToArray();

            var rolls = state.Settings.RollsVisibleToPlayers
                ? state.RollLog.TakeLast(10).Reverse().ToArray()
                : Array.Empty<RollResult>();

            var fogPath = FogPolygonBuilder.BuildSvgPathData(map);

            // Focus rect only drives the display viewBox while the active map
            // matches it — switching maps with a focus set elsewhere shouldn't
            // crop the new map to a stale rectangle.
            var focus = state.FocusRect is { } fr && fr.MapId == map.Id ? fr : null;

            return new DisplayProjection(map, images, tokens, map.MarkupSvg, state.ActiveCombat, rolls, fogPath, focus, activeTokenId);
        }

        // Active token id for the "whose turn is it" glow. Returns null unless
        // combat is in the Active phase and the current turn index points at a
        // combatant carrying a non-empty TokenId.
        private static Guid? ResolveActiveTurnTokenId(CombatState? combat)
        {
            if (combat is null || combat.Phase != CombatPhase.Active) return null;
            var turn = combat.TurnOrder;
            if (turn.Count == 0) return null;
            var idx = combat.CurrentTurnIndex;
            if (idx < 0 || idx >= turn.Count) return null;
            var tokenId = turn[idx].TokenId;
            return tokenId == Guid.Empty ? null : tokenId;
        }
    }
}
