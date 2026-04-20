namespace KnockBox.DiceSimulator.Services.State.Games.Data
{
    public sealed record DiceRollEntry
    {
        public required Guid Id { get; init; }
        public required string PlayerId { get; init; }
        public required string PlayerName { get; init; }
        public required DiceType DiceType { get; init; }
        public required int DiceCount { get; init; }
        public required int Modifier { get; init; }
        public required RollMode Mode { get; init; }
        public required int Result { get; init; }
        public required int[] RawRolls { get; init; }
        public required int[]? AltRolls { get; init; }
        public required int AltTotal { get; init; }
        public required string Expression { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
    }
}