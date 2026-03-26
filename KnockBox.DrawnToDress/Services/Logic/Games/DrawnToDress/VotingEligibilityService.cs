using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Determines which players are eligible to cast votes for a given head-to-head matchup.
    ///
    /// Eligibility rule: a player who is a direct participant in a matchup (i.e. their
    /// outfit is being judged) must not vote on that matchup, because they have a conflict
    /// of interest in choosing the winner.  All other registered players remain eligible.
    /// </summary>
    public static class VotingEligibilityService
    {
        /// <summary>
        /// Returns the set of player IDs that may cast votes for <paramref name="matchup"/>.
        /// The participants (<see cref="SwissMatchup.PlayerAId"/> and
        /// <see cref="SwissMatchup.PlayerBId"/>) are excluded; every other player in
        /// <paramref name="allPlayerIds"/> is considered eligible.
        /// </summary>
        /// <param name="matchup">The matchup being voted on.</param>
        /// <param name="allPlayerIds">All player IDs currently registered in the game.</param>
        public static IReadOnlySet<string> GetEligibleVoterIds(
            SwissMatchup matchup,
            IEnumerable<string> allPlayerIds)
        {
            var excluded = new HashSet<string> { matchup.PlayerAId, matchup.PlayerBId };
            return allPlayerIds
                .Where(id => !excluded.Contains(id))
                .ToHashSet();
        }

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="voterId"/> is allowed to
        /// vote on <paramref name="matchup"/> — that is, when the voter is not one of the
        /// matchup's participants.
        /// </summary>
        /// <param name="voterId">The player attempting to cast a vote.</param>
        /// <param name="matchup">The matchup being voted on.</param>
        public static bool IsEligibleToVote(string voterId, SwissMatchup matchup)
            => voterId != matchup.PlayerAId && voterId != matchup.PlayerBId;
    }
}
