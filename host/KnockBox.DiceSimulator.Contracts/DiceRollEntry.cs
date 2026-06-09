namespace KnockBox.DiceSimulator.Contracts
{
    /// <summary>
    /// An immutable record of a single dice roll. Created server-side by the engine
    /// and carried verbatim in the per-player <see cref="DiceSimulatorView"/> roll
    /// history (the roll log is public to all players in this game).
    /// </summary>
    public sealed record DiceRollEntry
    {
        public required Guid Id { get; init; }
        public required Guid PlayerId { get; init; }
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
