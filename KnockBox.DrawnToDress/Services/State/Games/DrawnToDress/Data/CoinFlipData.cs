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

    /// <summary>
    /// Distinguishes whether a coin flip resolves a criterion tie or a final standings tie.
    /// </summary>
    public enum CoinFlipContext
    {
        /// <summary>Resolves a tied voting criterion within a matchup.</summary>
        CriterionTie,

        /// <summary>Resolves a tied pair of players on the final leaderboard.</summary>
        FinalStandingsTie,
    }

    /// <summary>
    /// A single pending coin flip entry in the interactive coin flip queue.
    /// Supports both criterion ties and final standings ties.
    /// </summary>
    public class PendingCoinFlipEntry
    {
        /// <summary>Unique identifier for this flip entry.</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>The context in which this coin flip is being performed.</summary>
        public CoinFlipContext Context { get; set; }

        // ── Criterion tie fields ─────────────────────────────────────────────

        /// <summary>The matchup ID (for <see cref="CoinFlipContext.CriterionTie"/>).</summary>
        public Guid MatchupId { get; set; }

        /// <summary>The criterion ID (for <see cref="CoinFlipContext.CriterionTie"/>).</summary>
        public string CriterionId { get; set; } = string.Empty;

        /// <summary>Entrant A in the matchup (for <see cref="CoinFlipContext.CriterionTie"/>).</summary>
        public string EntrantAId { get; set; } = string.Empty;

        /// <summary>Entrant B in the matchup (for <see cref="CoinFlipContext.CriterionTie"/>).</summary>
        public string EntrantBId { get; set; } = string.Empty;

        // ── Final standings tie fields ───────────────────────────────────────

        /// <summary>Player A in the standings tie (for <see cref="CoinFlipContext.FinalStandingsTie"/>).</summary>
        public string PlayerAId { get; set; } = string.Empty;

        /// <summary>Player B in the standings tie (for <see cref="CoinFlipContext.FinalStandingsTie"/>).</summary>
        public string PlayerBId { get; set; } = string.Empty;

        // ── Caller / result tracking ─────────────────────────────────────────

        /// <summary>The player ID selected to call the coin flip.</summary>
        public string CallerPlayerId { get; set; } = string.Empty;

        /// <summary>Whether the caller chose heads (<see langword="true"/>) or tails (<see langword="false"/>).</summary>
        public bool CallerChoseHeads { get; set; }

        /// <summary>The server-generated coin flip result (heads = <see langword="true"/>).</summary>
        public bool ResultIsHeads { get; set; }

        /// <summary>The player/entrant ID of the winner of this flip.</summary>
        public string WinnerPlayerId { get; set; } = string.Empty;

        /// <summary>Whether this flip has been resolved.</summary>
        public bool IsResolved { get; set; }
    }
}
