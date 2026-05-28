namespace KnockBox.Codeword;

/// <summary>
/// The host-configurable match rules for a Codeword game. Held by
/// <see cref="Services.State.Games.CodewordGameState.Settings"/> and mutated via
/// <c>with</c> expressions inside the state's execute lock. Persisted to the
/// host's browser localStorage by the lobby page so a host's preferred rules
/// survive across sessions. Property-initializer form keeps it round-trippable
/// by System.Text.Json (Web defaults) via the parameterless constructor + init
/// setters.
/// </summary>
public sealed record CodewordSettings
{
    /// <summary>
    /// When <c>true</c>, the host is treated as a participant — included in
    /// role assignment, scoring, turn order, and the 4–8 participant count.
    /// Off by default, preserving the "host is the shared display" behavior.
    /// </summary>
    public bool HostPlaysGame { get; init; } = false;

    public bool EnableTimers { get; init; } = true;
    public int TotalGames { get; init; } = 5;

    public int SetupPhaseTimeoutMs { get; init; } = 5000;
    public int CluePhaseTimeoutMs { get; init; } = 30000;
    public int DiscussionPhaseTimeoutMs { get; init; } = 120000;
    public int VotePhaseTimeoutMs { get; init; } = 15000;
    public int RevealPhaseTimeoutMs { get; init; } = 10000;
    public int ContinueOrEndRoundPhaseTimeoutMs { get; init; } = 30000;
    public int InformantGuessTimeoutMs { get; init; } = 30000;
}
