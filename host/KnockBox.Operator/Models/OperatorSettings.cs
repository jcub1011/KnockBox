using System;

namespace KnockBox.Operator.Models;

public sealed record OperatorSettings
{
    public bool TimersEnabled { get; init; } = true;
    public TimeSpan SetupPhaseTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan PlayPhaseTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReactionPhaseTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan DrawPhaseTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int MaxHandSize { get; init; } = 5;
    public int MaxDrawPerTurn { get; init; } = 3;
    public decimal InitialPointsPositive { get; init; } = 10m;
    public decimal InitialPointsNegative { get; init; } = -10m;
    public TimeSpan NoReactionTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public bool EnableStacking { get; init; } = true;
    public bool FlipWinCondition { get; init; } = false;

    // When true the host is dealt in as a real participant rather than acting as the
    // shared display. This is a start-time choice driven by the lobby's "Start Game As
    // Player" button — it is intentionally not surfaced in the settings drawer and is
    // never persisted to localStorage.
    public bool HostPlays { get; init; } = false;
}
