namespace KnockBox.DndMapper.Services.Library
{
    public enum SlotKind { Auto, Manual }

    // Public DTO returned by ListSlotsAsync. The UI binds against this; the
    // internal SlotIndexEntry is persisted, this one is not.
    public sealed record SlotInfo(string Id, string Name, SlotKind Kind, DateTime UpdatedUtc);

    // Persisted record listing every slot. Stored as the single record in the
    // slots_index store (key = SlotsIndexKey).
    internal sealed record SlotsIndex
    {
        public List<SlotIndexEntry> Slots { get; init; } = [];
    }

    internal sealed record SlotIndexEntry
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public SlotKind Kind { get; init; } = SlotKind.Manual;
        public DateTime UpdatedUtc { get; init; }
    }
}
