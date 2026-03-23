namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    public class ClothingItem
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public required string CreatorId { get; init; }
        public required string CreatorName { get; init; }
        public required ClothingType Type { get; init; }
        public string SvgData { get; set; } = string.Empty;
    }
}
