namespace KnockBox.Operator.Contracts;

/// <summary>
/// The host-surfaced subset of Operator's match rules. Shared between the server
/// (mapped from the authoritative <c>OperatorSettings</c>) and the browser (the lobby
/// drawer renders + edits a copy, then sends it as the <c>update-settings</c> command
/// payload). Server-only fields (hand/draw limits, initial points, NoReactionTimeout,
/// EnableStacking, HostPlays) are intentionally omitted and preserved server-side on
/// apply. Timeouts are whole seconds on the wire (the server stores TimeSpans).
/// </summary>
public sealed record OperatorSettingsView
{
    public bool TimersEnabled { get; init; } = true;
    public int SetupPhaseSeconds { get; init; } = 60;
    public int PlayPhaseSeconds { get; init; } = 30;
    public int ReactionPhaseSeconds { get; init; } = 15;
    public bool FlipWinCondition { get; init; } = false;
}
