namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    /// <summary>
    /// Represents a player's position and score summary on the end-of-game leaderboard.
    /// </summary>
    public class LeaderboardEntry
    {
        /// <summary>The player's unique identifier.</summary>
        public string PlayerId { get; set; } = string.Empty;

        /// <summary>The player's display name.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Number of head-to-head matchup wins across all voting rounds.</summary>
        public int Wins { get; set; }

        /// <summary>Number of head-to-head matchup losses across all voting rounds.</summary>
        public int Losses { get; set; }

        /// <summary>Aggregated weighted voting score across all rounds and criteria.</summary>
        public double TotalScore { get; set; }

        /// <summary>Extra points awarded for achievements (e.g. completing an outfit on time).</summary>
        public int BonusPoints { get; set; }

        /// <summary>
        /// Weighted matchup wins used as a tiebreaker. Win=1.0, tie=0.5, loss=0.0 per matchup.
        /// </summary>
        public double MatchupWins { get; set; }

        /// <summary>Final rank on the leaderboard (1 = first place).</summary>
        public int Rank { get; set; }

        /// <summary>
        /// How this player's tie was broken, if applicable.
        /// <c>null</c> = no tiebreak needed, <c>"matchup_wins"</c> = broken by matchup wins,
        /// <c>"coin_flip"</c> = broken by coin flip.
        /// </summary>
        public string? TiebreakMethod { get; set; }
    }
}
