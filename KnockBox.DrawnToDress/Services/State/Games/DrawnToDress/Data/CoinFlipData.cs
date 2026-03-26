namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    /// <summary>
    /// Issued when a tie-break coin flip is required for a Swiss matchup.
    /// </summary>
    public class CoinFlipRequest
    {
        /// <summary>Unique identifier for this flip request.</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>The <see cref="SwissMatchup.Id"/> that is tied and requires a flip.</summary>
        public Guid MatchupId { get; set; }

        /// <summary>The player ID of the player who triggered the tie-break request.</summary>
        public string RequestingPlayerId { get; set; } = string.Empty;

        /// <summary>UTC timestamp when the request was created.</summary>
        public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Outcome of a <see cref="CoinFlipRequest"/> tie-break.
    /// </summary>
    public record CoinFlipResult(
        Guid RequestId,
        Guid MatchupId,
        bool IsHeads,
        string WinnerPlayerId);

    /// <summary>
    /// Records which entrant won a coin flip for a specific tied criterion within a matchup.
    /// </summary>
    public record CriterionCoinFlipResult(
        Guid MatchupId,
        string CriterionId,
        string WinnerEntrantId);
}
