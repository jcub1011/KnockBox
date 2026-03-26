using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Provides deterministic Swiss-system tournament pairing and round-count calculation
    /// for Drawn To Dress voting rounds.
    ///
    /// Entrant IDs encode player+round (e.g. "player1:1"). Pairing avoids matching a
    /// player's own outfits against each other when possible.
    /// </summary>
    public static class SwissTournamentService
    {
        // ── Round-count helpers ───────────────────────────────────────────────

        /// <summary>
        /// Returns the recommended number of Swiss rounds for <paramref name="entrantCount"/>
        /// entrants using the GDD's auto-calculated formula: ceil(log2(n)), with a minimum of 1.
        /// </summary>
        public static int CalculateRoundCount(int entrantCount)
        {
            if (entrantCount <= 2) return 1;
            return (int)Math.Ceiling(Math.Log2(entrantCount));
        }

        /// <summary>
        /// Resolves the effective round count for the session.
        /// </summary>
        public static int ResolveRoundCount(int entrantCount, int configuredRounds)
            => configuredRounds > 0 ? configuredRounds : CalculateRoundCount(entrantCount);

        // ── Win accounting ────────────────────────────────────────────────────

        /// <summary>
        /// Derives a win count for each entrant from the votes recorded across all matchups.
        /// The entrant who received strictly more criterion votes in a matchup is credited
        /// with one win. Ties award no win to either entrant.
        /// </summary>
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

                    int aVotes = matchupVotes.Count(v => v.ChosenEntrantId == matchup.EntrantAId);
                    int bVotes = matchupVotes.Count(v => v.ChosenEntrantId == matchup.EntrantBId);

                    if (aVotes > bVotes)
                        wins[matchup.EntrantAId] = wins.GetValueOrDefault(matchup.EntrantAId, 0) + 1;
                    else if (bVotes > aVotes)
                        wins[matchup.EntrantBId] = wins.GetValueOrDefault(matchup.EntrantBId, 0) + 1;
                }
            }

            return wins;
        }

        // ── Round generation ──────────────────────────────────────────────────

        /// <summary>
        /// Generates a single Swiss-system voting round.
        /// </summary>
        public static VotingRound GenerateRound(
            int roundNumber,
            IReadOnlyList<string> entrantIds,
            IReadOnlyList<VotingRound> previousRounds,
            IReadOnlyDictionary<string, int>? winsByPlayerId = null)
        {
            var wins = winsByPlayerId ?? new Dictionary<string, int>();
            var previousPairs = CollectPreviousPairs(previousRounds);

            // Sort: highest wins first; break ties by entrant ID for determinism.
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

        private static HashSet<(string, string)> CollectPreviousPairs(
            IReadOnlyList<VotingRound> previousRounds)
        {
            var pairs = new HashSet<(string, string)>();
            foreach (var round in previousRounds)
                foreach (var matchup in round.Matchups)
                    pairs.Add(NormalizedPair(matchup.EntrantAId, matchup.EntrantBId));
            return pairs;
        }

        /// <summary>
        /// Greedy Swiss pairing with same-player avoidance.
        /// Avoids pairing two entrants from the same player when possible.
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

                string aPlayer = DrawnToDressGameContext.GetPlayerIdFromEntrantId(a);

                // Find the best opponent:
                // 1. Not a rematch AND not the same player
                // 2. Not a rematch (allow same player)
                // 3. Not the same player (allow rematch)
                // 4. Closest available (fallback)
                int bIndex = -1;

                // Pass 1: non-rematch, different player
                for (int i = 0; i < unpaired.Count; i++)
                {
                    string bPlayer = DrawnToDressGameContext.GetPlayerIdFromEntrantId(unpaired[i]);
                    if (bPlayer != aPlayer && !previousPairs.Contains(NormalizedPair(a, unpaired[i])))
                    {
                        bIndex = i;
                        break;
                    }
                }

                // Pass 2: non-rematch (may be same player)
                if (bIndex == -1)
                {
                    for (int i = 0; i < unpaired.Count; i++)
                    {
                        if (!previousPairs.Contains(NormalizedPair(a, unpaired[i])))
                        {
                            bIndex = i;
                            break;
                        }
                    }
                }

                // Pass 3: different player (may be rematch)
                if (bIndex == -1)
                {
                    for (int i = 0; i < unpaired.Count; i++)
                    {
                        string bPlayer = DrawnToDressGameContext.GetPlayerIdFromEntrantId(unpaired[i]);
                        if (bPlayer != aPlayer)
                        {
                            bIndex = i;
                            break;
                        }
                    }
                }

                // Pass 4: take the closest one
                if (bIndex == -1)
                    bIndex = 0;

                string b = unpaired[bIndex];
                unpaired.RemoveAt(bIndex);

                matchups.Add(new SwissMatchup(Guid.NewGuid(), a, b, roundNumber));
            }

            return matchups;
        }

        private static (string, string) NormalizedPair(string a, string b)
            => string.Compare(a, b, StringComparison.Ordinal) < 0 ? (a, b) : (b, a);
    }
}
