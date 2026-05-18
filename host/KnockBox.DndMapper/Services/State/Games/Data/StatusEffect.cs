namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class StatusEffect
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<AttributeDelta> AttributeDeltas { get; set; } = [];
        public int? MaxHpDelta { get; set; }
        public int? OnApplyHpDelta { get; set; }
        public string Notes { get; set; } = string.Empty;
        public DateTime AppliedUtc { get; set; }
    }
}
