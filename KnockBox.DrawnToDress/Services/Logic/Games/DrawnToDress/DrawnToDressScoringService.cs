using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress
{
    /// <summary>
    /// Pure scoring functions for Drawn To Dress. Converts persisted votes into weighted
    /// criterion scores, matchup totals, round bonuses, player totals, and leaderboard entries.
    /// </summary>
    public static class DrawnToDressScoringService
    {
        /// <summary>
        /// Returns (aScore, bScore) for a single criterion on a single matchup.
        /// Score = vote count × criterion weight.
        /// </summary>
        public static (double AScore, double BScore) CalculateCriterionScores(
            SwissMatchup matchup,
            string criterionId,
            double weight,
            IEnumerable<VoteSubmission> votes)
        {
            var criterionVotes = votes
                .Where(v => v.MatchupId == matchup.Id && v.CriterionId == criterionId)
                .ToList();

            int aVotes = criterionVotes.Count(v => v.ChosenEntrantId == matchup.EntrantAId);
            int bVotes = criterionVotes.Count(v => v.ChosenEntrantId == matchup.EntrantBId);

            return (aVotes * weight, bVotes * weight);
        }

        /// <summary>
        /// Returns all (matchupId, criterionId) pairs where votes are exactly tied
        /// (including 0-0) for the given round, excluding criteria that already have a
        /// coin flip result.
        /// </summary>
        public static List<(Guid MatchupId, string CriterionId)> FindTiedCriteria(
            VotingRound round,
            IReadOnlyList<VotingCriterionDefinition> criteria,
            IEnumerable<VoteSubmission> votes,
            IReadOnlyList<CriterionCoinFlipResult>? existingFlipResults = null)
        {
            var voteList = votes.ToList();
            var existing = existingFlipResults ?? [];
            var ties = new List<(Guid, string)>();

            foreach (var matchup in round.Matchups)
            {
                var matchupVotes = voteList
                    .Where(v => v.MatchupId == matchup.Id)
                    .ToList();

                foreach (var criterion in criteria)
                {
                    // Skip if already resolved by a previous coin flip.
                    if (existing.Any(r => r.MatchupId == matchup.Id && r.CriterionId == criterion.Id))
                        continue;

                    var criterionVotes = matchupVotes
                        .Where(v => v.CriterionId == criterion.Id)
                        .ToList();

                    int aVotes = criterionVotes.Count(v => v.ChosenEntrantId == matchup.EntrantAId);
                    int bVotes = criterionVotes.Count(v => v.ChosenEntrantId == matchup.EntrantBId);

                    if (aVotes == bVotes)
                        ties.Add((matchup.Id, criterion.Id));
                }
            }

            return ties;
        }

        /// <summary>
        /// Calculates matchup totals for both entrants: sum of criterion scores + coin flip bonuses.
        /// Coin flip bonus is +1 flat (not multiplied by weight).
        /// </summary>
        public static (double ATotal, double BTotal) CalculateMatchupTotals(
            SwissMatchup matchup,
            IReadOnlyList<VotingCriterionDefinition> criteria,
            IEnumerable<VoteSubmission> votes,
            IReadOnlyList<CriterionCoinFlipResult> coinFlipResults)
        {
            var voteList = votes.ToList();
            double aTotal = 0;
            double bTotal = 0;

            foreach (var criterion in criteria)
            {
                var (aScore, bScore) = CalculateCriterionScores(matchup, criterion.Id, criterion.Weight, voteList);
                aTotal += aScore;
                bTotal += bScore;
            }

            // Add coin flip bonuses (+1 flat per flip win).
            foreach (var flip in coinFlipResults.Where(f => f.MatchupId == matchup.Id))
            {
                if (flip.WinnerEntrantId == matchup.EntrantAId)
                    aTotal += 1;
                else if (flip.WinnerEntrantId == matchup.EntrantBId)
                    bTotal += 1;
            }

            return (aTotal, bTotal);
        }

        /// <summary>
        /// Calculates total points per entrant for a given round (across all matchups).
        /// </summary>
        public static Dictionary<EntrantId, double> CalculateRoundScores(
            VotingRound round,
            IReadOnlyList<VotingCriterionDefinition> criteria,
            IEnumerable<VoteSubmission> votes,
            IReadOnlyList<CriterionCoinFlipResult> coinFlipResults)
        {
            var voteList = votes.ToList();
            var scores = new Dictionary<EntrantId, double>();

            foreach (var matchup in round.Matchups)
            {
                var (aTotal, bTotal) = CalculateMatchupTotals(matchup, criteria, voteList, coinFlipResults);
                scores[matchup.EntrantAId] = scores.GetValueOrDefault(matchup.EntrantAId) + aTotal;
                scores[matchup.EntrantBId] = scores.GetValueOrDefault(matchup.EntrantBId) + bTotal;
            }

            return scores;
        }

        /// <summary>
        /// Returns the set of entrant IDs with the highest score in the round.
        /// Handles ties (all tied leaders are returned).
        /// </summary>
        public static HashSet<EntrantId> GetRoundLeaders(Dictionary<EntrantId, double> roundScores)
        {
            if (roundScores.Count == 0) return [];

            double maxScore = roundScores.Values.Max();
            return roundScores
                .Where(kv => kv.Value == maxScore)
                .Select(kv => kv.Key)
                .ToHashSet();
        }

        /// <summary>
        /// Calculates matchup win/tie/loss values per entrant across all rounds.
        /// Win=1.0, tie=0.5, loss=0.0 per matchup.
        /// </summary>
        public static Dictionary<EntrantId, double> CalculateMatchupWins(
            IReadOnlyList<VotingRound> rounds,
            IReadOnlyList<VotingCriterionDefinition> criteria,
            IEnumerable<VoteSubmission> votes,
            IReadOnlyList<CriterionCoinFlipResult> coinFlipResults)
        {
            var voteList = votes.ToList();
            var wins = new Dictionary<EntrantId, double>();

            foreach (var round in rounds)
            {
                foreach (var matchup in round.Matchups)
                {
                    var (aTotal, bTotal) = CalculateMatchupTotals(matchup, criteria, voteList, coinFlipResults);

                    // Ensure both entrants have entries.
                    wins.TryAdd(matchup.EntrantAId, 0);
                    wins.TryAdd(matchup.EntrantBId, 0);

                    if (aTotal > bTotal)
                        wins[matchup.EntrantAId] += 1.0;
                    else if (bTotal > aTotal)
                        wins[matchup.EntrantBId] += 1.0;
                    else
                    {
                        // Tie: both get 0.5.
                        wins[matchup.EntrantAId] += 0.5;
                        wins[matchup.EntrantBId] += 0.5;
                    }
                }

                // Bye entrants receive a free win.
                foreach (var byeEntrant in round.Byes)
                {
                    wins.TryAdd(byeEntrant, 0);
                    wins[byeEntrant] += 1.0;
                }
            }

            return wins;
        }

        /// <summary>
        /// Rolls up entrant-level scores to player-level totals.
        /// Player total = sum of all entrant scores + player BonusPoints.
        /// </summary>
        public static Dictionary<string, double> CalculatePlayerTotals(
            IReadOnlyList<VotingRound> rounds,
            IReadOnlyList<VotingCriterionDefinition> criteria,
            IEnumerable<VoteSubmission> votes,
            IReadOnlyList<CriterionCoinFlipResult> coinFlipResults,
            IReadOnlyDictionary<string, DrawnToDressPlayerState> players,
            DrawnToDressConfig config)
        {
            var voteList = votes.ToList();
            var playerTotals = new Dictionary<string, double>();

            // Sum entrant scores across all rounds.
            foreach (var round in rounds)
            {
                var roundScores = CalculateRoundScores(round, criteria, voteList, coinFlipResults);
                foreach (var (entrantId, score) in roundScores)
                {
                    playerTotals[entrantId.PlayerId] = playerTotals.GetValueOrDefault(entrantId.PlayerId) + score;
                }
            }

            // Add player bonus points (e.g. outfit completion bonus, round leader bonus).
            foreach (var (playerId, playerState) in players)
            {
                playerTotals[playerId] = playerTotals.GetValueOrDefault(playerId) + playerState.BonusPoints;
            }

            return playerTotals;
        }

        /// <summary>
        /// Builds the final ranked leaderboard. Ranking: total points (desc) → matchup wins (desc).
        /// Returns the leaderboard entries and a list of ties that may need coin flip resolution.
        /// </summary>
        public static (List<LeaderboardEntry> Entries, List<(string PlayerA, string PlayerB)> TiedPairs)
            BuildLeaderboard(
                IReadOnlyList<VotingRound> rounds,
                IReadOnlyList<VotingCriterionDefinition> criteria,
                IEnumerable<VoteSubmission> votes,
                IReadOnlyList<CriterionCoinFlipResult> coinFlipResults,
                IReadOnlyDictionary<string, DrawnToDressPlayerState> players,
                DrawnToDressConfig config)
        {
            var voteList = votes.ToList();
            var playerTotals = CalculatePlayerTotals(rounds, criteria, voteList, coinFlipResults, players, config);
            var entrantMatchupWins = CalculateMatchupWins(rounds, criteria, voteList, coinFlipResults);

            // Roll up matchup wins to player level.
            var playerMatchupWins = new Dictionary<string, double>();
            foreach (var (entrantId, mw) in entrantMatchupWins)
            {
                playerMatchupWins[entrantId.PlayerId] = playerMatchupWins.GetValueOrDefault(entrantId.PlayerId) + mw;
            }

            // Calculate Swiss W/L from SwissTournamentService for display.
            var swissWins = SwissTournamentService.CalculateWins(rounds, voteList);
            var playerSwissWins = new Dictionary<string, int>();
            var playerSwissLosses = new Dictionary<string, int>();
            // Count total matchups per entrant to derive losses.
            var totalMatchupsPerEntrant = new Dictionary<EntrantId, int>();
            foreach (var round in rounds)
            {
                foreach (var matchup in round.Matchups)
                {
                    totalMatchupsPerEntrant[matchup.EntrantAId] = totalMatchupsPerEntrant.GetValueOrDefault(matchup.EntrantAId) + 1;
                    totalMatchupsPerEntrant[matchup.EntrantBId] = totalMatchupsPerEntrant.GetValueOrDefault(matchup.EntrantBId) + 1;
                }
            }

            foreach (var (entrantId, w) in swissWins)
            {
                var pid = entrantId.PlayerId;
                playerSwissWins[pid] = playerSwissWins.GetValueOrDefault(pid) + w;
            }

            foreach (var (entrantId, total) in totalMatchupsPerEntrant)
            {
                var pid = entrantId.PlayerId;
                int entrantWins = swissWins.GetValueOrDefault(entrantId);
                int entrantLosses = total - entrantWins;
                playerSwissLosses[pid] = playerSwissLosses.GetValueOrDefault(pid) + entrantLosses;
            }

            // Count byes per player.
            var playerByeCount = new Dictionary<string, int>();
            foreach (var round in rounds)
            {
                foreach (var byeEntrant in round.Byes)
                {
                    var pid = byeEntrant.PlayerId;
                    playerByeCount[pid] = playerByeCount.GetValueOrDefault(pid) + 1;
                }
            }

            // Build entries.
            var entries = new List<LeaderboardEntry>();
            foreach (var (playerId, playerState) in players)
            {
                entries.Add(new LeaderboardEntry
                {
                    PlayerId = playerId,
                    DisplayName = playerState.DisplayName,
                    TotalScore = playerTotals.GetValueOrDefault(playerId),
                    BonusPoints = playerState.BonusPoints,
                    MatchupWins = playerMatchupWins.GetValueOrDefault(playerId),
                    Wins = playerSwissWins.GetValueOrDefault(playerId),
                    Losses = playerSwissLosses.GetValueOrDefault(playerId),
                    ByeCount = playerByeCount.GetValueOrDefault(playerId),
                });
            }

            // Sort by total score desc, then matchup wins desc.
            entries = entries
                .OrderByDescending(e => e.TotalScore)
                .ThenByDescending(e => e.MatchupWins)
                .ThenBy(e => e.PlayerId, StringComparer.Ordinal)
                .ToList();

            // Assign ranks (tied players share the same rank).
            var tiedPairs = new List<(string, string)>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i == 0)
                {
                    entries[i].Rank = 1;
                }
                else if (entries[i].TotalScore == entries[i - 1].TotalScore &&
                         entries[i].MatchupWins == entries[i - 1].MatchupWins)
                {
                    entries[i].Rank = entries[i - 1].Rank;
                    tiedPairs.Add((entries[i - 1].PlayerId, entries[i].PlayerId));
                }
                else
                {
                    entries[i].Rank = i + 1;
                }
            }

            return (entries, tiedPairs);
        }

        /// <summary>
        /// Re-ranks leaderboard entries that were tied, using resolved coin flip results
        /// from <see cref="PendingCoinFlipEntry"/> entries with
        /// <see cref="CoinFlipContext.FinalStandingsTie"/>.
        /// Winners of the coin flip get the better (lower) rank.
        /// Sets <see cref="LeaderboardEntry.TiebreakMethod"/> to <c>"coin_flip"</c>.
        /// </summary>
        public static void ApplyCoinFlipTiebreaks(
            List<LeaderboardEntry> entries,
            List<PendingCoinFlipEntry> resolvedFlips)
        {
            var standingsFlips = resolvedFlips
                .Where(f => f.IsResolved && f.Context == CoinFlipContext.FinalStandingsTie)
                .ToList();

            if (standingsFlips.Count == 0) return;

            // Build a set of winners and losers from the flips.
            var flipWinners = new Dictionary<(string, string), string>();
            foreach (var flip in standingsFlips)
            {
                flipWinners[(flip.PlayerAId, flip.PlayerBId)] = flip.WinnerPlayerId;
                flipWinners[(flip.PlayerBId, flip.PlayerAId)] = flip.WinnerPlayerId;
            }

            // Group entries by rank (tied groups).
            var groups = entries.GroupBy(e => e.Rank).Where(g => g.Count() > 1).ToList();

            foreach (var group in groups)
            {
                var members = group.ToList();
                int baseRank = group.Key;

                // Find the start index of this group in the list.
                int startIdx = entries.IndexOf(members[0]);

                // Sort within the tied group: coin flip winners first.
                members.Sort((a, b) =>
                {
                    if (flipWinners.TryGetValue((a.PlayerId, b.PlayerId), out var winner))
                    {
                        return winner == a.PlayerId ? -1 : 1;
                    }
                    return string.Compare(a.PlayerId, b.PlayerId, StringComparison.Ordinal);
                });

                // Replace the group entries in the list with the sorted order.
                for (int i = 0; i < members.Count; i++)
                {
                    entries[startIdx + i] = members[i];
                    members[i].Rank = baseRank + i;
                    members[i].TiebreakMethod = "coin_flip";
                }
            }

            // Also set matchup_wins for entries that were NOT tied.
            SetMatchupWinsTiebreakMethod(entries);
        }

        /// <summary>
        /// Sets <see cref="LeaderboardEntry.TiebreakMethod"/> to <c>"matchup_wins"</c>
        /// for entries that share the same <see cref="LeaderboardEntry.TotalScore"/> with
        /// an adjacent entry but have different <see cref="LeaderboardEntry.MatchupWins"/>
        /// (i.e. matchup wins already broke the tie).
        /// </summary>
        public static void SetMatchupWinsTiebreakMethod(List<LeaderboardEntry> entries)
        {
            for (int i = 1; i < entries.Count; i++)
            {
                if (entries[i].TiebreakMethod is not null) continue;
                if (entries[i - 1].TiebreakMethod is not null) continue;

                // Check if these two share the same total score but different matchup wins.
                if (entries[i].TotalScore == entries[i - 1].TotalScore &&
                    entries[i].MatchupWins != entries[i - 1].MatchupWins)
                {
                    entries[i].TiebreakMethod = "matchup_wins";
                    entries[i - 1].TiebreakMethod = "matchup_wins";
                }
            }
        }
    }
}
