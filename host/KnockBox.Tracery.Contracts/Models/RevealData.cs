using System.Collections.Immutable;

namespace KnockBox.Tracery.Models
{
    /// <summary>
    /// The fully-assembled set of reveal beats for one completed round (GDD §7), produced by
    /// <c>RevealBuilder</c> at round close and stored on <c>TraceryGameState.CurrentReveal</c>.
    /// Everything here is pre-computed so <c>TraceryRevealView</c> is a pure renderer — it never
    /// recomputes scores or re-solves the board. Beats that can't exist (e.g. nobody banked a
    /// word) are null/empty rather than throwing, so the view simply skips them.
    /// </summary>
    public sealed record RevealData
    {
        public int RoundNumber { get; init; }

        /// <summary>The longest word anyone banked, with everyone who found it. Null if no word was banked.</summary>
        public RevealWordBeat? LongestWord { get; init; }

        /// <summary>The single highest-scoring banked word of the round. Null if no word was banked.</summary>
        public RevealWordBeat? HighestScoringWord { get; init; }

        /// <summary>
        /// Notable words on the board that no player banked, longest/most-valuable first
        /// (sorted by would-be unique score). Sourced from the solver's complete findable set.
        /// </summary>
        public ImmutableArray<MissedWord> WordsNobodyFound { get; init; } = [];

        /// <summary>The highest-value rare letters that appeared in banked words, richest first.</summary>
        public ImmutableArray<RareLetterUse> RarestLetters { get; init; } = [];

        /// <summary>
        /// The score one player would have earned by banking the entire findable set as unique
        /// finds (GDD §7 benchmark). Null when the <c>ShowTheoreticalMax</c> setting is off.
        /// </summary>
        public int? TheoreticalMax { get; init; }

        /// <summary>Per-player round points plus running cumulative, highest cumulative first.</summary>
        public ImmutableArray<StandingRow> Standings { get; init; } = [];
    }

    /// <summary>
    /// A spotlighted word and the player(s) who banked it. Used for both the longest-word and
    /// highest-scoring-word beats. <see cref="Finders"/> lists every player who banked the word —
    /// usually one, but more when several share it (e.g. two players tie on the longest word).
    /// </summary>
    public sealed record RevealWordBeat
    {
        public string Word { get; init; } = string.Empty;

        /// <summary>The word's length (the metric for the longest-word beat).</summary>
        public int Length { get; init; }

        /// <summary>Points the word earned (the metric for the highest-scoring-word beat).</summary>
        public int Points { get; init; }

        /// <summary>True if exactly one player banked the word this round.</summary>
        public bool IsUnique { get; init; }

        /// <summary>Display names of every player who banked the word, ordered alphabetically.</summary>
        public ImmutableArray<string> Finders { get; init; } = [];

        /// <summary>A representative grid path that spells the word, for highlighting on the board.</summary>
        public ImmutableArray<int> Path { get; init; } = [];
    }

    /// <summary>A findable word that no player banked, with the score it would have paid as a unique find.</summary>
    public sealed record MissedWord
    {
        public string Word { get; init; } = string.Empty;
        public int WouldBeScore { get; init; }

        /// <summary>A representative grid path that spells the word, for highlighting on the board.</summary>
        public ImmutableArray<int> Path { get; init; } = [];
    }

    /// <summary>A rare letter put to use in a banked word, with its bonus value and an example word.</summary>
    public sealed record RareLetterUse
    {
        public char Letter { get; init; }
        public int BonusValue { get; init; }
        public string ExampleWord { get; init; } = string.Empty;
    }

    /// <summary>One player's line on the standings beat.</summary>
    public sealed record StandingRow
    {
        public Guid UserId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public int RoundPoints { get; init; }
        public int CumulativeScore { get; init; }
    }
}
