using System.Collections.Immutable;
using KnockBox.Tracery.Models;

namespace KnockBox.Tracery.Services.Logic
{
    /// <summary>
    /// Pure assembly of the host reveal (GDD §7) from the round's two finished data sources: the
    /// solver's complete findable set (the board's full word list, from M04) and the scored
    /// <see cref="RoundResult"/> (every player's banked words + points, from M06). It recomputes
    /// <em>nothing</em> — points come straight off <see cref="TraceryWordScore"/>; the only fresh
    /// arithmetic is the would-be score of words nobody banked and the theoretical maximum, both
    /// of which are scored as unique finds via <see cref="TraceryScorer"/>. Static and
    /// side-effect-free so it is exhaustively unit-testable from a fixed grid + scripted banks.
    /// </summary>
    public static class RevealBuilder
    {
        // How many notable beats to surface. The view shows a leaderboard-style shortlist, not the
        // whole board — these caps keep the reveal readable on the host screen.
        private const int MaxWordsNobodyFound = 10;
        private const int MaxRareLetters = 5;

        /// <summary>
        /// Assembles the reveal for one completed round from the round's two findable sets.
        /// </summary>
        /// <param name="validationSet">
        /// Every word findable under the answer (validation) dictionary — the complete bankable
        /// set. Source of truth for the theoretical maximum and for the path of any banked beat
        /// (a banked word is always answer-valid, so its path lives here). A player's score can
        /// never exceed the theoretical max derived from it.
        /// </param>
        /// <param name="boardSet">
        /// Words findable under the board (generation) dictionary — the common-word set the board
        /// was built from. Source for "words nobody found", so that list stays recognizable even
        /// when answers allow obscure words. Equals <paramref name="validationSet"/> when the two
        /// dictionaries resolve to the same pool.
        /// </param>
        /// <param name="roundResult">The scored outcomes for the round (per-player banked words + points).</param>
        /// <param name="settings">Match settings — supplies the scoring tables and the theoretical-max toggle.</param>
        public static RevealData Build(
            IReadOnlyDictionary<string, TracedWord> validationSet,
            IReadOnlyDictionary<string, TracedWord> boardSet,
            RoundResult roundResult,
            TracerySettings settings)
        {
            ArgumentNullException.ThrowIfNull(validationSet);
            ArgumentNullException.ThrowIfNull(boardSet);
            ArgumentNullException.ThrowIfNull(roundResult);
            ArgumentNullException.ThrowIfNull(settings);

            // Flatten every (player, banked word) pair once; most beats are projections of this.
            var bankedEntries = roundResult.Outcomes
                .SelectMany(o => o.WordScores.Select(w => (Outcome: o, Score: w)))
                .ToList();

            return new RevealData
            {
                RoundNumber = roundResult.RoundNumber,
                LongestWord = BuildLongestWord(bankedEntries, validationSet),
                HighestScoringWord = BuildHighestScoringWord(bankedEntries, validationSet),
                WordsNobodyFound = BuildWordsNobodyFound(bankedEntries, boardSet, settings),
                RarestLetters = BuildRarestLetters(bankedEntries, settings),
                TheoreticalMax = settings.ShowTheoreticalMax
                    ? TheoreticalMaximum(validationSet, settings)
                    : null,
                Standings = BuildStandings(roundResult),
            };
        }

        // The longest banked word. Ties between distinct words break by higher points, then
        // alphabetically (ordinal) — deterministic so a fixed round always reveals the same word.
        // The chosen word may have been banked by several players; all of them are listed.
        private static RevealWordBeat? BuildLongestWord(
            List<(TraceryPlayerRoundOutcome Outcome, TraceryWordScore Score)> banked,
            IReadOnlyDictionary<string, TracedWord> findableSet)
        {
            if (banked.Count == 0) return null;

            var best = banked
                .OrderByDescending(e => e.Score.Word.Length)
                .ThenByDescending(e => e.Score.Points)
                .ThenBy(e => e.Score.Word, StringComparer.Ordinal)
                .First().Score;

            return WordBeat(best, banked, findableSet);
        }

        // The single highest-scoring banked word. Ties break by length, then alphabetically.
        private static RevealWordBeat? BuildHighestScoringWord(
            List<(TraceryPlayerRoundOutcome Outcome, TraceryWordScore Score)> banked,
            IReadOnlyDictionary<string, TracedWord> findableSet)
        {
            if (banked.Count == 0) return null;

            var best = banked
                .OrderByDescending(e => e.Score.Points)
                .ThenByDescending(e => e.Score.Word.Length)
                .ThenBy(e => e.Score.Word, StringComparer.Ordinal)
                .First().Score;

            return WordBeat(best, banked, findableSet);
        }

        // Builds a beat for a chosen word: gathers every finder (all players who banked it) and a
        // representative path from the findable set. The unique flag and points come off the
        // chosen TraceryWordScore (whichever player's was selected — they agree for a shared word).
        private static RevealWordBeat WordBeat(
            TraceryWordScore chosen,
            List<(TraceryPlayerRoundOutcome Outcome, TraceryWordScore Score)> banked,
            IReadOnlyDictionary<string, TracedWord> findableSet)
        {
            var finders = banked
                .Where(e => e.Score.Word == chosen.Word)
                .Select(e => e.Outcome.DisplayName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();

            return new RevealWordBeat
            {
                Word = chosen.Word,
                Length = chosen.Word.Length,
                Points = chosen.Points,
                IsUnique = chosen.IsUnique,
                Finders = finders,
                Path = PathFor(chosen.Word, findableSet),
            };
        }

        // findableSet keys minus the union of all banked words = the words on the board nobody
        // found. Each is scored as it would have paid for a lone finder (unique), and the richest
        // are surfaced first so the long/rare lurkers lead.
        private static ImmutableArray<MissedWord> BuildWordsNobodyFound(
            List<(TraceryPlayerRoundOutcome Outcome, TraceryWordScore Score)> banked,
            IReadOnlyDictionary<string, TracedWord> findableSet,
            TracerySettings settings)
        {
            var found = banked.Select(e => e.Score.Word).ToHashSet(StringComparer.Ordinal);

            return findableSet.Keys
                .Where(w => !found.Contains(w))
                .Select(w => new MissedWord
                {
                    Word = w,
                    WouldBeScore = TraceryScorer.WordScore(w, isUnique: true, settings),
                    Path = PathFor(w, findableSet),
                })
                .OrderByDescending(m => m.WouldBeScore)
                .ThenByDescending(m => m.Word.Length)
                .ThenBy(m => m.Word, StringComparer.Ordinal)
                .Take(MaxWordsNobodyFound)
                .ToImmutableArray();
        }

        // The highest-value rare letters that actually appeared in banked words, one row per
        // distinct letter. The example is the longest banked word using that letter (ties broken
        // alphabetically), so the showcase word feels worth the rarity.
        private static ImmutableArray<RareLetterUse> BuildRarestLetters(
            List<(TraceryPlayerRoundOutcome Outcome, TraceryWordScore Score)> banked,
            TracerySettings settings)
        {
            var table = settings.RareLetterBonusTable;
            if (!settings.RareLetterBonusEnabled || table is null || table.Count == 0)
                return [];

            // Distinct banked words, longest first, so the first word seen per letter is its best example.
            var words = banked
                .Select(e => e.Score.Word)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(w => w.Length)
                .ThenBy(w => w, StringComparer.Ordinal)
                .ToList();

            var byLetter = new Dictionary<char, RareLetterUse>();
            foreach (var word in words)
            {
                foreach (char c in word)
                {
                    char upper = char.ToUpperInvariant(c);
                    if (byLetter.ContainsKey(upper)) continue;
                    if (table.TryGetValue(upper, out int bonus))
                        byLetter[upper] = new RareLetterUse
                        {
                            Letter = upper,
                            BonusValue = bonus,
                            ExampleWord = word,
                        };
                }
            }

            return byLetter.Values
                .OrderByDescending(r => r.BonusValue)
                .ThenBy(r => r.Letter)
                .Take(MaxRareLetters)
                .ToImmutableArray();
        }

        // The score for banking the entire board as unique finds — the best any single player
        // could theoretically have done (GDD §7 benchmark).
        private static int TheoreticalMaximum(
            IReadOnlyDictionary<string, TracedWord> findableSet,
            TracerySettings settings)
            => findableSet.Keys.Sum(w => TraceryScorer.WordScore(w, isUnique: true, settings));

        private static ImmutableArray<StandingRow> BuildStandings(RoundResult roundResult)
            => roundResult.Outcomes
                .Select(o => new StandingRow
                {
                    UserId = o.UserId,
                    DisplayName = o.DisplayName,
                    RoundPoints = o.PointsAwarded,
                    CumulativeScore = o.CumulativeScore,
                })
                .OrderByDescending(s => s.CumulativeScore)
                .ThenByDescending(s => s.RoundPoints)
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();

        private static ImmutableArray<int> PathFor(string word, IReadOnlyDictionary<string, TracedWord> findableSet)
            => findableSet.TryGetValue(word, out var traced)
                ? traced.Path.ToImmutableArray()
                : [];
    }
}
