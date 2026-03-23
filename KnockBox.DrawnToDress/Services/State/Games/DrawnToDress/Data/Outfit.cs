namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    public class Outfit
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public required string PlayerId { get; init; }
        public required string PlayerName { get; init; }
        public required int OutfitNumber { get; init; }
        public Dictionary<ClothingType, ClothingItem?> Items { get; } = new()
        {
            [ClothingType.Hat] = null,
            [ClothingType.Shirt] = null,
            [ClothingType.Pants] = null,
            [ClothingType.Shoes] = null,
        };
        public string? SketchData { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsLocked { get; set; }
        public bool IsSubmitted { get; set; }

        public bool IsComplete => Items.Values.All(v => v is not null);

        public IEnumerable<Guid> ItemIds =>
            Items.Values.Where(v => v is not null).Select(v => v!.Id);
    }
}
