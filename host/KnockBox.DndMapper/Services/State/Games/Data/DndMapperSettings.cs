using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    // Init-only properties (record) enforce the "wholesale replace, never mutate
    // in place" contract that DndMapperLibraryService's auto-save fingerprint
    // depends on — the fingerprint stores a reference to Settings, so an in-
    // place field write would invisibly bypass the dirty check and silently
    // drop persisted edits. Use `with` expressions to derive a modified copy.
    public sealed record DndMapperSettings
    {
        public TokenMovementPolicy TokenMovement { get; init; } = TokenMovementPolicy.OwnerOrHost;
        public SheetEditPolicy SheetEditByOthers { get; init; } = SheetEditPolicy.OwnersAndHost;
        public bool RollsVisibleToPlayers { get; init; } = true;
        public bool PlayersCanCreateNPCs { get; init; } = false;
        public bool HpTrackingEnabled { get; init; } = true;
        public bool PlayersCanSeeOtherSheets { get; init; } = false;
    }
}
