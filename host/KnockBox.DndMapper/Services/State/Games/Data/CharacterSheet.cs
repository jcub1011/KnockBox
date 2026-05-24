using System.Collections.Immutable;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record CharacterSheet
    {
        public Guid Id { get; init; }
        public string? OwnerUserId { get; init; }
        // Set when a player leaves mid-session and their sheet is orphaned —
        // mirrors Token.RepresentsUserId so the UI can show "originally played by …"
        // until the host reassigns the character to another player.
        public string? RepresentsUserId { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public ImmutableDictionary<string, AttributeValue> Values { get; init; }
            = ImmutableDictionary<string, AttributeValue>.Empty;
        public string Notes { get; init; } = string.Empty;
        public int? Hp { get; init; }
        public int? MaxHp { get; init; }
        public ImmutableList<StatusEffect> StatusEffects { get; init; } = ImmutableList<StatusEffect>.Empty;
        public ImmutableList<RollTemplate> RollTemplates { get; init; } = ImmutableList<RollTemplate>.Empty;
    }
}
