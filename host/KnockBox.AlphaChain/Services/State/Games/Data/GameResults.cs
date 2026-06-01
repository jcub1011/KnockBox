using System.Collections.Generic;

namespace KnockBox.AlphaChain.Services.State.Games.Data
{
    /// <summary>
    /// Final standings populated by <c>GameOverState</c> and consumed by the results
    /// screen. A skeleton in M1 (rank by score); scoring detail fills in from M2.
    /// </summary>
    public record GameResults(IReadOnlyList<PlayerResult> Standings);

    /// <summary>A single row of the final standings.</summary>
    public record PlayerResult(string UserId, string DisplayName, int Score, int Rank);
}
