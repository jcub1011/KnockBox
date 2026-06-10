namespace KnockBox.LinkedList.Contracts
{
    /// <summary>
    /// The host-editable subset of a Linked List match's rules. Doubles as the
    /// <see cref="LinkedListCommands.UpdateSettings"/> command payload (host → server) and a
    /// field on <see cref="LinkedListView"/> (server → every client). It deliberately omits the
    /// server-only <c>HostPlays</c> flag — that's a start-time choice settled by which start
    /// button the host clicks, so the server's <c>LinkedListSettings</c> keeps it and this maps
    /// to/from the rest. <see cref="PerTurnClockSeconds"/> is the wire form of the server's
    /// <c>PerTurnClock</c> <see cref="System.TimeSpan"/>. Init-only properties keep it
    /// round-trippable by System.Text.Json via the parameterless ctor.
    /// </summary>
    public sealed record LinkedListSettingsView
    {
        public ScoringMode ScoringMode { get; init; } = ScoringMode.FewestGuesses;
        public PlayerStructure PlayerStructure { get; init; } = PlayerStructure.Collective;

        /// <summary>Rejected attempts allowed per turn before forfeit. 0 = off (unlimited).</summary>
        public int RejectionCap { get; init; } = 3;

        /// <summary>Block re-forming a pair already in the chain (a loop). Off by default.</summary>
        public bool NoImmediateRepeat { get; init; } = false;

        /// <summary>Collective co-op target the host sets by hand (§8.1). Null = no par.</summary>
        public int? Par { get; init; } = null;

        /// <summary>Rounds played before the match ends and the Results screen shows (§10).</summary>
        public int RoundsPerMatch { get; init; } = 5;

        /// <summary>Per-turn thinking budget, in seconds (server holds a <c>TimeSpan</c>).</summary>
        public int PerTurnClockSeconds { get; init; } = 60;

        public bool EnableTimers { get; init; } = true;
    }
}
