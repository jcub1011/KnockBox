namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class CharacterSheet
    {
        public Guid Id { get; set; }
        public string? OwnerUserId { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public Dictionary<string, AttributeValue> Values { get; } = [];
        public string Notes { get; set; } = string.Empty;
        public int? Hp { get; set; }
        public int? MaxHp { get; set; }
    }
}
