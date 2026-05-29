using System.Collections.Immutable;

namespace KnockBox.Tracery.Models
{
    /// <summary>
    /// Immutable record of a single completed round, appended to
    /// <c>TraceryGameState.RoundResults</c> when the round closes. Minimal this milestone
    /// (round number + per-player points); extended with reveal data — longest word,
    /// findable set, unique finds — in Milestone 06.
    /// </summary>
    public sealed record RoundResult
    {
        public int RoundNumber { get; init; }
        public ImmutableArray<TraceryPlayerRoundOutcome> Outcomes { get; init; } = [];
    }

    /// <summary>One player's outcome within a single round.</summary>
    public sealed record TraceryPlayerRoundOutcome
    {
        public string UserId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public int PointsAwarded { get; init; }
    }
}
