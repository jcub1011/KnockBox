using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Provides deterministic Swiss-system tournament pairing and round-count calculation
    /// for Drawn To Dress voting rounds.
    ///
    /// Swiss pairing rules applied here:
    /// - Round 1: entrants are ordered by ID (deterministic seed) and paired sequentially.
    /// - Round 2+: entrants are sorted by descending win count, then by ID as a tiebreaker,
    ///   and paired sequentially while avoiding rematches where possible.
    /// - Self-matchups are never generated (a player cannot face themselves).
    /// - If all remaining opponents are rematches the closest available is used (rematch
    ///   is preferable to a bye).
    /// </summary>
    public static class SwissTournamentService
    {
        // ── Round-count helpers ───────────────────────────────────────────────

        /// <summary>
        /// Returns the recommended number of Swiss rounds for <paramref name="entrantCount"/>
        /// entrants using the GDD's auto-calculated formula: ⌈log₂(n)⌉, with a minimum of 1.
        /// </summary>
        /// <param name="entrantCount">Number of players who have submitted at least one outfit.</param>
        public static int CalculateRoundCount(int entrantCount)
        {
            if (entrantCount <= 2) return 1;
            return (int)Math.Ceiling(Math.Log2(entrantCount));
        }

        /// <summary>
        /// Resolves the effective round count for the session.
        /// When <paramref name="configuredRounds"/> is positive the configured value wins;
        /// when it is zero the auto-calculated value is used instead.
        /// </summary>
        /// <param name="entrantCount">Number of entrants (players with a submitted outfit).</param>
        /// <param name="configuredRounds">
        /// The value from <see cref="DrawnToDressConfig.VotingRounds"/>.
        /// Pass <c>0</c> to enable auto-calculation.
        /// </param>
        public static int ResolveRoundCount(int entrantCount, int configuredRounds)
            => configuredRounds > 0 ? configuredRounds : CalculateRoundCount(entrantCount);

        // ── Win accounting ────────────────────────────────────────────────────

        /// <summary>
        /// Derives a win count for each entrant from the votes recorded in
        /// <paramref name="votes"/> across all matchups in <paramref name="previousRounds"/>.
        ///
        /// The player who received strictly more criterion votes in a matchup is credited
        /// with one win. Ties award no win to either player (a later coin-flip state
        /// resolves ties for scoring purposes).
        /// </summary>
        /// <returns>Dictionary of player ID → win count; entrants with no wins are omitted.</returns>
        public static Dictionary<string, int> CalculateWins(
            IReadOnlyList<VotingRound> previousRounds,
            IEnumerable<VoteSubmission> votes)
        {
            var wins = new Dictionary<string, int>();
            var voteList = votes.ToList();

            foreach (var round in previousRounds)
            {
                foreach (var matchup in round.Matchups)
                {
                    var matchupVotes = voteList
                        .Where(v => v.MatchupId == matchup.Id)
                        .ToList();

                    if (matchupVotes.Count == 0) continue;

                    int aVotes = matchupVotes.Count(v => v.ChosenPlayerId == matchup.PlayerAId);
                    int bVotes = matchupVotes.Count(v => v.ChosenPlayerId == matchup.PlayerBId);

                    if (aVotes > bVotes)
                        wins[matchup.PlayerAId] = wins.GetValueOrDefault(matchup.PlayerAId, 0) + 1;
                    else if (bVotes > aVotes)
                        wins[matchup.PlayerBId] = wins.GetValueOrDefault(matchup.PlayerBId, 0) + 1;
                    // Tie: no win awarded for pairing purposes.
                }
            }

            return wins;
        }

        // ── Round generation ──────────────────────────────────────────────────

        /// <summary>
        /// Generates a single Swiss-system voting round.
        /// </summary>
        /// <param name="roundNumber">1-based round index.</param>
        /// <param name="entrantIds">
        /// All entrant player IDs participating in the tournament.
        /// Only players with at least one submitted outfit should be included.
        /// </param>
        /// <param name="previousRounds">All rounds already completed (used to avoid rematches).</param>
        /// <param name="winsByPlayerId">
        /// Win counts per player as computed by <see cref="CalculateWins"/>.
        /// Pass an empty dictionary for round 1.
        /// </param>
        public static VotingRound GenerateRound(
            int roundNumber,
            IReadOnlyList<string> entrantIds,
            IReadOnlyList<VotingRound> previousRounds,
            IReadOnlyDictionary<string, int>? winsByPlayerId = null)
        {
            var wins = winsByPlayerId ?? new Dictionary<string, int>();
            var previousPairs = CollectPreviousPairs(previousRounds);

            // Sort: highest wins first; break ties by player ID for determinism.
            var sorted = entrantIds
                .OrderByDescending(id => wins.TryGetValue(id, out var w) ? w : 0)
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToList();

            var matchups = PairGreedy(sorted, previousPairs, roundNumber);

            return new VotingRound
            {
                RoundNumber = roundNumber,
                Matchups = matchups,
            };
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Builds the set of player-pair keys for all matchups already completed.
        /// The pair key is always stored with the lexicographically-smaller ID first,
        /// making lookup symmetric.
        /// </summary>
        private static HashSet<(string, string)> CollectPreviousPairs(
            IReadOnlyList<VotingRound> previousRounds)
        {
            var pairs = new HashSet<(string, string)>();
            foreach (var round in previousRounds)
                foreach (var matchup in round.Matchups)
                    pairs.Add(NormalizedPair(matchup.PlayerAId, matchup.PlayerBId));
            return pairs;
        }

        /// <summary>
        /// Greedy Swiss pairing: iterates through the sorted entrant list and assigns
        /// each unpaired player the highest-ranked opponent they have not yet faced.
        /// Falls back to the closest available opponent when all remaining candidates
        /// are rematches (preferable to leaving a player without a matchup).
        /// Players who have no opponent (odd-count situations) receive no matchup in this round.
        /// </summary>
        private static List<SwissMatchup> PairGreedy(
            List<string> sorted,
            HashSet<(string, string)> previousPairs,
            int roundNumber)
        {
            var matchups = new List<SwissMatchup>();
            var unpaired = new List<string>(sorted);

            while (unpaired.Count >= 2)
            {
                string a = unpaired[0];
                unpaired.RemoveAt(0);

                // Find the best opponent (first non-rematch in the sorted list).
                int bIndex = -1;
                for (int i = 0; i < unpaired.Count; i++)
                {
                    if (!previousPairs.Contains(NormalizedPair(a, unpaired[i])))
                    {
                        bIndex = i;
                        break;
                    }
                }

                // All remaining candidates are rematches → take the closest one.
                if (bIndex == -1)
                    bIndex = 0;

                string b = unpaired[bIndex];
                unpaired.RemoveAt(bIndex);

                matchups.Add(new SwissMatchup(Guid.NewGuid(), a, b, roundNumber));
            }

            return matchups;
        }

        /// <summary>
        /// Returns a canonical pair tuple where the lexicographically-smaller ID is always first.
        /// </summary>
        private static (string, string) NormalizedPair(string a, string b)
            => string.Compare(a, b, StringComparison.Ordinal) < 0 ? (a, b) : (b, a);
    }
}
