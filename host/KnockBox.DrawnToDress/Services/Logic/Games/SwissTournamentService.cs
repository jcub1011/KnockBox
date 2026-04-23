using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Services.Logic.Games
{
    /// <summary>
    /// Provides deterministic Swiss-system tournament pairing and round-count calculation
    /// for Drawn To Dress voting rounds.
    ///
    /// Pairing avoids matching a player's own outfits against each other when possible.
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
        /// Win=1.0, tie=0.5, loss=0.0 per matchup.
        /// </summary>
        public static Dictionary<EntrantId, double> CalculateWins(
            IReadOnlyList<VotingRound> previousRounds,
            IEnumerable<VoteSubmission> votes)
        {
            var wins = new Dictionary<EntrantId, double>();
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
                        wins[matchup.EntrantAId] = wins.GetValueOrDefault(matchup.EntrantAId, 0.0) + 1.0;
                    else if (bVotes > aVotes)
                        wins[matchup.EntrantBId] = wins.GetValueOrDefault(matchup.EntrantBId, 0.0) + 1.0;
                    else
                    {
                        // Tie: both get 0.5.
                        wins[matchup.EntrantAId] = wins.GetValueOrDefault(matchup.EntrantAId, 0.0) + 0.5;
                        wins[matchup.EntrantBId] = wins.GetValueOrDefault(matchup.EntrantBId, 0.0) + 0.5;
                    }
                }

                // Bye entrants receive a free win.
                foreach (var byeEntrant in round.Byes)
                    wins[byeEntrant] = wins.GetValueOrDefault(byeEntrant, 0.0) + 1.0;
            }

            return wins;
        }

        // ── Round generation ──────────────────────────────────────────────────

        /// <summary>
        /// Generates a single Swiss-system voting round.
        /// When there is an odd number of entrants, the lowest-ranked entrant
        /// who has not yet received a bye is given a bye (free win).
        /// </summary>
        public static VotingRound GenerateRound(
            int roundNumber,
            IReadOnlyList<EntrantId> entrantIds,
            IReadOnlyList<VotingRound> previousRounds,
            IReadOnlyDictionary<EntrantId, double>? winsByEntrantId = null)
        {
            var wins = winsByEntrantId ?? new Dictionary<EntrantId, double>();
            var previousPairs = CollectPreviousPairs(previousRounds);

            // Sort: highest wins first; break ties by entrant ID string for determinism.
            var sorted = entrantIds
                .OrderByDescending(id => wins.TryGetValue(id, out var w) ? w : 0.0)
                .ThenBy(id => id.ToString(), StringComparer.Ordinal)
                .ToList();

            // Handle bye for odd entrant count.
            var byes = new List<EntrantId>();
            if (sorted.Count % 2 == 1 && sorted.Count >= 1)
            {
                var previousByeSet = previousRounds
                    .SelectMany(r => r.Byes)
                    .ToHashSet();

                // Pick the lowest-ranked entrant who hasn't had a bye yet.
                int byeIndex = sorted.FindLastIndex(id => !previousByeSet.Contains(id));
                var byeCandidate = byeIndex >= 0 ? sorted[byeIndex] : sorted[^1];
                sorted.Remove(byeCandidate);
                byes.Add(byeCandidate);
            }

            var matchups = PairGreedy(sorted, previousPairs, roundNumber);

            return new VotingRound
            {
                RoundNumber = roundNumber,
                Matchups = matchups,
                Byes = byes,
            };
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static HashSet<(EntrantId, EntrantId)> CollectPreviousPairs(
            IReadOnlyList<VotingRound> previousRounds)
        {
            var pairs = new HashSet<(EntrantId, EntrantId)>();
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
            List<EntrantId> sorted,
            HashSet<(EntrantId, EntrantId)> previousPairs,
            int roundNumber)
        {
            var matchups = new List<SwissMatchup>();
            var unpaired = new List<EntrantId>(sorted);

            while (unpaired.Count >= 2)
            {
                var a = unpaired[0];
                unpaired.RemoveAt(0);

                string aPlayer = a.PlayerId;

                // Find the best opponent:
                // 1. Not a rematch AND not the same player
                // 2. Not a rematch (allow same player)
                // 3. Not the same player (allow rematch)
                // 4. Closest available (fallback)
                int bIndex = -1;

                // Pass 1: non-rematch, different player
                for (int i = 0; i < unpaired.Count; i++)
                {
                    if (unpaired[i].PlayerId != aPlayer && !previousPairs.Contains(NormalizedPair(a, unpaired[i])))
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
                        if (unpaired[i].PlayerId != aPlayer)
                        {
                            bIndex = i;
                            break;
                        }
                    }
                }

                // Pass 4: take the closest one
                if (bIndex == -1)
                    bIndex = 0;

                var b = unpaired[bIndex];
                unpaired.RemoveAt(bIndex);

                matchups.Add(new SwissMatchup(Guid.NewGuid(), a, b, roundNumber));
            }

            return matchups;
        }

        private static (EntrantId, EntrantId) NormalizedPair(EntrantId a, EntrantId b)
            => string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal) < 0 ? (a, b) : (b, a);
    }
}
