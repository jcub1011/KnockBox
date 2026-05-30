namespace KnockBox.LinkedList;

/// <summary>How a finished round/match is scored.</summary>
public enum ScoringMode { FewestGuesses, FastestTime }

/// <summary>Whether everyone shares one chain or competes in groups.</summary>
public enum PlayerStructure { Collective, Groups }

/// <summary>
/// Host-configurable match rules for a Linked List game. Held by
/// <see cref="Services.State.Games.LinkedListGameState.Settings"/> and replaced
/// atomically via <c>with</c> expressions inside the state's execute lock
/// (see <c>LinkedListGameState.UpdateSettings</c>). Property-initializer form
/// keeps it round-trippable by System.Text.Json (Web defaults) via the
/// parameterless constructor + init setters.
/// </summary>
public sealed record LinkedListSettings
{
    public ScoringMode ScoringMode { get; init; } = ScoringMode.FewestGuesses;
    public PlayerStructure PlayerStructure { get; init; } = PlayerStructure.Collective;

    /// <summary>Rejected attempts allowed per turn before forfeit. 0 = off (unlimited).</summary>
    public int RejectionCap { get; init; } = 3;

    /// <summary>Optional §7.4 rigor: block a pair identical to the immediately previous pair.</summary>
    public bool NoImmediateRepeat { get; init; } = false;

    public bool HostPlaysGame { get; init; } = false;

    /// <summary>Collective co-op target the host sets by hand (§8.1). Null = no par.</summary>
    public int? Par { get; init; } = null;

    /// <summary>Rounds played before the match ends and the Results screen shows (§10).
    /// The Auditor rotates each round, so this also controls how many players audit.</summary>
    public int RoundsPerMatch { get; init; } = 5;

    // Timer durations used in Milestone 3 (defined now so the record is stable).
    public TimeSpan PerTurnClock { get; init; } = TimeSpan.FromSeconds(60);
    public bool EnableTimers { get; init; } = true;
}
