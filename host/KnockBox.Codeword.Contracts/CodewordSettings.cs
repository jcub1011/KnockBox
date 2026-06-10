namespace KnockBox.Codeword.Contracts;

/// <summary>
/// The host-configurable match rules for a Codeword game. Shared between the server
/// (authoritative <c>state.Settings</c>) and the browser (the House Rules drawer renders
/// + edits a copy, then sends it as the <c>update-settings</c> command payload, which the
/// server deserializes straight back into this type). Property-initializer form keeps it
/// round-trippable by System.Text.Json via the parameterless constructor + init setters.
/// </summary>
public sealed record CodewordSettings
{
    /// <summary>
    /// When <c>true</c>, the host is treated as a participant — included in
    /// role assignment, scoring, turn order, and the 4–8 participant count.
    /// Off by default, preserving the "host is the shared display" behavior.
    /// Set by the lobby's deal buttons (not the House Rules drawer).
    /// </summary>
    public bool HostPlays { get; init; } = false;

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
