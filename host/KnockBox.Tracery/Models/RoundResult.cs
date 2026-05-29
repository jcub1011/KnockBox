using System.Collections.Immutable;

namespace KnockBox.Tracery.Models
{
    /// <summary>
    /// Immutable record of a single completed round, appended to
    /// <c>TraceryGameState.RoundResults</c> when the round closes (Milestone 04). Milestone 06
    /// fills in the scoring: each outcome carries the round/cumulative totals and a per-word
    /// breakdown so the reveal (Milestone 07) renders the numbers directly rather than
    /// recomputing them.
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

        /// <summary>Points earned this round (sum of <see cref="WordScores"/> points).</summary>
        public int PointsAwarded { get; init; }

        /// <summary>The player's cumulative score after this round was scored.</summary>
        public int CumulativeScore { get; init; }

        /// <summary>Per-word scoring breakdown for everything the player banked this round.</summary>
        public ImmutableArray<TraceryWordScore> WordScores { get; init; } = [];

        // ── Search mode ──────────────────────────────────────────────────────
        /// <summary>
        /// Search mode: the player's 1-based finishing place (1 = first to find the whole list), or
        /// null if they did not complete the search list this round. Null in Standard mode.
        /// </summary>
        public int? CompletionRank { get; init; }

        /// <summary>Search mode: the placement bonus included in <see cref="PointsAwarded"/> (0 if not completed).</summary>
        public int CompletionBonus { get; init; }

        /// <summary>Search mode: how many of the search list's words this player found.</summary>
        public int WordsFound { get; init; }

        /// <summary>Search mode: the size of the round's shared search list (the target to "complete").</summary>
        public int SearchListSize { get; init; }
    }

    /// <summary>
    /// The scoring breakdown for a single banked word (GDD §5). <see cref="Points"/> is the final
    /// awarded value after the unique-find multiplier; the component fields are retained so the
    /// reveal can show how the total was built up.
    /// </summary>
    public sealed record TraceryWordScore
    {
        public string Word { get; init; } = string.Empty;

        /// <summary>Base score — the word's length (GDD §5.1).</summary>
        public int BaseScore { get; init; }

        /// <summary>Superlinear length bonus (GDD §5.2).</summary>
        public int LengthBonus { get; init; }

        /// <summary>Rare-letter bonus, summed per qualifying letter occurrence (GDD §5.3).</summary>
        public int RareLetterBonus { get; init; }

        /// <summary>True if no other player banked this word this round (GDD §5.4).</summary>
        public bool IsUnique { get; init; }

        /// <summary>
        /// Final points awarded: <c>round((Base + LengthBonus + RareLetter) × multiplier)</c>,
        /// where the multiplier applies only to unique finds when the bonus is enabled.
        /// </summary>
        public int Points { get; init; }
    }
}
