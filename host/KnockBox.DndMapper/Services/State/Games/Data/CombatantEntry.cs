namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record CombatantEntry
    {
        public Guid Id { get; init; }
        public Guid TokenId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? OwnerUserId { get; init; }
        public int? InitiativeRoll { get; init; }
        public bool IsForceRolled { get; init; }
    }
}
