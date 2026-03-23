namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    public class PlayerScore
    {
        public required string PlayerId { get; init; }
        public required string PlayerName { get; init; }
        public int TotalPoints { get; set; }
        public List<Guid> OutfitIds { get; } = new();
    }
}
