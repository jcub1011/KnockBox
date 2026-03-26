using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Determines which players are eligible to cast votes for a given head-to-head matchup.
    ///
    /// Eligibility rule: a player who created either entrant's outfit must not vote on
    /// that matchup, because they have a conflict of interest in choosing the winner.
    /// All other registered players remain eligible.
    /// </summary>
    public static class VotingEligibilityService
    {
        /// <summary>
        /// Returns the set of player IDs that may cast votes for <paramref name="matchup"/>.
        /// The creators of both entrants are excluded; every other player in
        /// <paramref name="allPlayerIds"/> is considered eligible.
        /// </summary>
        public static IReadOnlySet<string> GetEligibleVoterIds(
            SwissMatchup matchup,
            IEnumerable<string> allPlayerIds)
        {
            var excluded = new HashSet<string>
            {
                DrawnToDressGameContext.GetPlayerIdFromEntrantId(matchup.EntrantAId),
                DrawnToDressGameContext.GetPlayerIdFromEntrantId(matchup.EntrantBId),
            };
            return allPlayerIds
                .Where(id => !excluded.Contains(id))
                .ToHashSet();
        }

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="voterId"/> is allowed to
        /// vote on <paramref name="matchup"/> — that is, when the voter is not the creator
        /// of either entrant.
        /// </summary>
        public static bool IsEligibleToVote(string voterId, SwissMatchup matchup)
        {
            var playerA = DrawnToDressGameContext.GetPlayerIdFromEntrantId(matchup.EntrantAId);
            var playerB = DrawnToDressGameContext.GetPlayerIdFromEntrantId(matchup.EntrantBId);
            return voterId != playerA && voterId != playerB;
        }
    }
}
