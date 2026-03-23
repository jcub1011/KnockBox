namespace KnockBox.Services.State.Games.DrawnToDress.Data
{
    public class VotingMatchup
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public required Guid OutfitAId { get; init; }
        public required Guid OutfitBId { get; init; }
        public required int VotingRound { get; init; }

        /// <summary>
        /// Votes per criterion: criterion -> (votesForA, votesForB).
        /// </summary>
        public Dictionary<VotingCriterion, (int VotesA, int VotesB)> CriterionVotes { get; } = new();

        /// <summary>
        /// Set of player IDs that have already voted on this matchup.
        /// </summary>
        public HashSet<string> VotedPlayerIds { get; } = new();

        /// <summary>
        /// Points awarded per criterion: criterion -> (pointsA, pointsB).
        /// Populated after the matchup is finalized.
        /// </summary>
        public Dictionary<VotingCriterion, (int PointsA, int PointsB)> CriterionPoints { get; } = new();

        public bool IsComplete { get; set; }

        public int TotalPointsA => CriterionPoints.Values.Sum(p => p.PointsA);
        public int TotalPointsB => CriterionPoints.Values.Sum(p => p.PointsB);
    }
}
