namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class CharacterSheet
    {
        public Guid Id { get; set; }
        public string? OwnerUserId { get; set; }
        // Set when a player leaves mid-session and their sheet is orphaned —
        // mirrors Token.RepresentsUserId so the UI can show "originally played by …"
        // until the host reassigns the character to another player.
        public string? RepresentsUserId { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public Dictionary<string, AttributeValue> Values { get; } = [];
        public string Notes { get; set; } = string.Empty;
        public int? Hp { get; set; }
        public int? MaxHp { get; set; }
        public List<StatusEffect> StatusEffects { get; } = [];
    }
}
