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
        // construction, so their Rows are not serialised; their StatusEffectTemplates
        // are.
        public bool IsBuiltIn { get; set; }
        // Host-authored status-effect templates scoped to this schema. Switching
        // the active schema hides templates from other schemas; deleting this
        // template cascades the list away.
        public List<StatusEffectTemplate> StatusEffectTemplates { get; set; } = [];
        // Attribute name used as the initiative modifier under this schema.
        // Null means "fall back to DEX case-insensitively" (legacy behaviour).
        // The selector lives on the Combat panel and writes through the engine.
        public string? InitiativeAttributeName { get; set; }
    }
}
