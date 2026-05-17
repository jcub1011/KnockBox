namespace KnockBox.DndMapper.Services.State.Games.Data
{
    // A host-saved attribute template. Selecting a template swaps the active
    // AttributeSchema to one built from its Rows (under the Custom preset).
    public sealed class NamedTemplate
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<AttributeRow> Rows { get; set; } = [];
        // True for first-party presets seeded at session start. Built-ins cannot
        // be edited or deleted by the host and are re-seeded on every state
        // construction, so they're skipped when serializing library snapshots.
        public bool IsBuiltIn { get; set; }
    }
}
