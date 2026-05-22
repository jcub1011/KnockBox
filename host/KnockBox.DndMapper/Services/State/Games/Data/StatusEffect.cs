using System.Collections.Immutable;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record StatusEffect
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public ImmutableList<AttributeDelta> AttributeDeltas { get; init; } = ImmutableList<AttributeDelta>.Empty;
        public int? MaxHpDelta { get; init; }
        public int? OnApplyHpDelta { get; init; }
        public string Notes { get; init; } = string.Empty;
        public DateTime AppliedUtc { get; init; }
    }
}
