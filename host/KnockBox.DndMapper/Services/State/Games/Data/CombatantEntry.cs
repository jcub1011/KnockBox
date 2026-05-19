namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class CombatantEntry
    {
        public Guid Id { get; set; }
        public Guid TokenId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? OwnerUserId { get; set; }
        public int? InitiativeRoll { get; set; }
        public bool IsForceRolled { get; set; }
    }
}
