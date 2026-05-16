namespace KnockBox.DndMapper.Services.State.Games.Data
{
    // A host-saved attribute template. Selecting a template swaps the active
    // AttributeSchema to one built from its Rows (under the Custom preset).
    public sealed class NamedTemplate
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<AttributeRow> Rows { get; set; } = [];
    }
}
