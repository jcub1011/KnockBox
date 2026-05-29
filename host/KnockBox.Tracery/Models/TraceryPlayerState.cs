using System.Collections.Immutable;

namespace KnockBox.Tracery.Models
{
    /// <summary>
    /// Per-player, per-match data. One instance is created per participant at game start
    /// and lives on <c>TraceryGameState</c> for the match duration. Writes are owned by
    /// the engine and only ever happen inside <c>Execute</c>/<c>ExecuteAsync</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors Spardle's <c>PlayerState</c>: cumulative score persists across rounds while
    /// round-scoped fields are cleared by <see cref="ResetRound"/> at the start of each
    /// round. <see cref="BankedWords"/> is the set of words accepted this round; Milestone 05
    /// fills it via <c>TraceryGameEngine.SubmitTrace</c> and Milestone 06 reads it for scoring.
    /// </remarks>
    public sealed class TraceryPlayerState
    {
        /// <summary>Total score accumulated across all completed rounds.</summary>
        public int CumulativeScore { get; set; }

        /// <summary>Points earned in the most recently completed round (for the results screen).</summary>
        public int LastRoundPoints { get; set; }

        /// <summary>Points earned so far in the current (in-progress) round.</summary>
        public int RoundScore { get; set; }

        /// <summary>
        /// Words this player has banked in the current round, keyed by the lower-cased word so a
        /// re-trace of the same word is a cheap O(1) duplicate check (GDD §4: a word scores once
        /// per player per round, regardless of which path spelled it). The stored
        /// <see cref="TracedWord"/> carries the accepted path for the reveal animation; Milestone
        /// 06 attaches the point value at round close. See <see cref="BankedInOrder"/> for the
        /// acceptance-ordered view the in-game list renders.
        /// </summary>
        public ImmutableDictionary<string, TracedWord> BankedWords { get; private set; }
            = ImmutableDictionary<string, TracedWord>.Empty;

        /// <summary>
        /// The same banks as <see cref="BankedWords"/>, kept in the order they were accepted this
        /// round so the in-game list can show them most-recent-first. The dictionary remains the
        /// O(1) duplicate check; this is purely for ordered display.
        /// </summary>
        public ImmutableList<TracedWord> BankedInOrder { get; private set; }
            = ImmutableList<TracedWord>.Empty;

        /// <summary>True if <paramref name="word"/> is already banked this round.</summary>
        public bool HasBanked(string word) => BankedWords.ContainsKey(word);

        /// <summary>
        /// Records <paramref name="traced"/> as banked. Idempotent: re-banking the same word
        /// keeps the first accepted path. Like the other mutators, only called by the engine
        /// inside its execute lock.
        /// </summary>
        public void Bank(TracedWord traced)
        {
            if (!BankedWords.ContainsKey(traced.Word))
            {
                BankedWords = BankedWords.Add(traced.Word, traced);
                BankedInOrder = BankedInOrder.Add(traced);
            }
        }

        /// <summary>Clears the round-scoped fields. Call at the start of each round.</summary>
        public void ResetRound()
        {
            RoundScore = 0;
            LastRoundPoints = 0;
            BankedWords = BankedWords.Clear();
            BankedInOrder = BankedInOrder.Clear();
        }
    }
}
