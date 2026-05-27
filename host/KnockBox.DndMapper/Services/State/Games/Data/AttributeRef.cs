namespace KnockBox.DndMapper.Services.State.Games.Data
{
    // Associates a roll with a character sheet, optionally pulling one of
    // its attribute modifiers into the total. AttributeName is null/empty
    // when the roll is "for this sheet" but no attribute mod is applied
    // (e.g. host picks a sheet in the From-sheet dropdown but leaves the
    // attribute selector blank). Loaded-dice rule matching uses SheetId
    // either way; the engine's attribute-resolution path is gated on the
    // name being non-empty.
    public sealed record AttributeRef(Guid SheetId, string? AttributeName);
}
