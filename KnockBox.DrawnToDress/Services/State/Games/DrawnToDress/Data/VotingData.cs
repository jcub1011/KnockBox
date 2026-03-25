namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    /// <summary>
    /// Defines a single axis on which outfits are judged during voting (e.g. Creativity).
    /// Each criterion carries a relative weight used for score aggregation.
    /// </summary>
    public class VotingCriterionDefinition
    {
        /// <summary>Unique identifier for this criterion (e.g. "creativity").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Human-readable label shown to voters (e.g. "Creativity").</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Relative weight used when aggregating scores across criteria. Higher values
        /// contribute more to the final score. All weights are summed and each criterion's
        /// contribution is proportional to its share of the total.
        /// </summary>
        public double Weight { get; set; } = 1.0;
    }

    /// <summary>
    /// Represents a single head-to-head matchup between two players in a Swiss voting round.
    /// </summary>
    public record SwissMatchup(
        Guid Id,
        string PlayerAId,
        string PlayerBId,
        int RoundNumber);

    /// <summary>
    /// Represents one round of Swiss-system voting, containing the set of head-to-head
    /// matchups that take place in that round.
    /// </summary>
    public class VotingRound
    {
        /// <summary>1-based round index.</summary>
        public int RoundNumber { get; set; }

        /// <summary>All head-to-head matchups for this round.</summary>
        public List<SwissMatchup> Matchups { get; set; } = [];
    }

    /// <summary>
    /// Records a single vote cast by one player during a voting round.
    /// Distinguishes the matchup, the voter, the criterion being judged, and the chosen outfit.
    /// </summary>
    public class VoteSubmission
    {
        /// <summary>The player ID of the voter.</summary>
        public string VoterPlayerId { get; set; } = string.Empty;

        /// <summary>The <see cref="SwissMatchup.Id"/> this vote applies to.</summary>
        public Guid MatchupId { get; set; }

        /// <summary>
        /// The criterion being voted on (matches <see cref="VotingCriterionDefinition.Id"/>).
        /// </summary>
        public string CriterionId { get; set; } = string.Empty;

        /// <summary>The player ID of the outfit the voter chose as winner on this criterion.</summary>
        public string ChosenPlayerId { get; set; } = string.Empty;

        /// <summary>UTC timestamp when the vote was submitted.</summary>
        public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// <see langword="true"/> when the vote was submitted after the round deadline.
        /// Late votes may still be counted but could receive a score penalty depending on config.
        /// </summary>
        public bool IsLate { get; set; }
    }
}
