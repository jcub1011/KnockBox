namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// An immutable snapshot of a single submission, built once per word and handed to
    /// every modifier's <c>Trigger</c>/<c>Value</c> delegate. Keeping it small and
    /// value-only means triggers can be evaluated cheaply and—critically—without
    /// capturing live game state: a card lambda never reaches back into the room.
    /// <para>
    /// The first six members are the word's intrinsic shape; the trailing members are
    /// turn-context the time-aware and meta cards read (<see cref="RemainingSeconds"/> for
    /// Sprinter/Panic Button, <see cref="MultiplierScale"/> for Hyper-Drive). They carry
    /// neutral defaults so ad-hoc/test contexts and the legacy two-argument
    /// <see cref="Build(string, char?)"/> stay valid.
    /// </para>
    /// </summary>
    /// <param name="Word">The normalized (trimmed, lower-case) word.</param>
    /// <param name="Length">Letter count of <paramref name="Word"/>.</param>
    /// <param name="Vowels">Number of vowels (a, e, i, o, u) in the word.</param>
    /// <param name="Consonants">Number of consonant letters in the word.</param>
    /// <param name="BannedLetter">The match's banned letter (lower-case), or null when unset.</param>
    /// <param name="ContainsBannedLetter">True when <paramref name="Word"/> contains the banned letter.</param>
    public sealed record WordContext(
        string Word,
        int Length,
        int Vowels,
        int Consonants,
        char? BannedLetter,
        bool ContainsBannedLetter)
    {
        private const string VowelSet = "aeiou";

        /// <summary>Letters that count as <i>both</i> vowel and consonant under The Catalyst.</summary>
        private const string CatalystAmbiguous = "ywh";

        /// <summary>Seconds left on the submitter's shot clock at the moment of submission.
        /// Read by time-aware cards (Sprinter, Panic Button). Defaults to 0.</summary>
        public double RemainingSeconds { get; init; }

        /// <summary>The match's configured base shot-clock length, in seconds. Defaults to 0.</summary>
        public int ShotClockSeconds { get; init; }

        /// <summary>
        /// Scale applied to every <i>multiplicative</i> card's factor in the scoring pipeline.
        /// 1.0 normally; Hyper-Drive raises it (e.g. ×2) for the rest of an era so "all
        /// multipliers are doubled" without touching any individual card. Defaults to 1.
        /// </summary>
        public double MultiplierScale { get; init; } = 1.0;

        /// <summary>
        /// True when the word contains a double letter — two equal letters adjacent (the 'ff' in
        /// <i>coffin</i>). Read by The Double Down (×2 vs ×0.5) and Scattershot's targeting.
        /// </summary>
        public bool HasDoubleLetter { get; init; }

        /// <summary>
        /// The owner's live Titanium Mirror multiplier at submit time (1.0 normally; lower once the
        /// shield has deflected attacks this era). The Titanium Mirror card reads this as its
        /// scoring factor, so the decaying shield folds into the pipeline like any other ×. Defaults to 1.
        /// </summary>
        public double ShieldMultiplier { get; init; } = 1.0;

        /// <summary>
        /// Builds a <see cref="WordContext"/> from a normalized word and the match's banned
        /// letter. Counts vowels/consonants over ASCII letters only; non-letter characters
        /// (which a dictionary word should not contain) are ignored by both counters.
        /// </summary>
        public static WordContext Build(string normalizedWord, char? bannedLetter)
            => Build(normalizedWord, bannedLetter, remainingSeconds: 0, shotClockSeconds: 0, multiplierScale: 1.0);

        /// <summary>
        /// Builds a <see cref="WordContext"/> with full turn context for time-aware and meta
        /// cards. <paramref name="remainingSeconds"/> is the submitter's clock at submit time,
        /// <paramref name="shotClockSeconds"/> the configured base, <paramref name="multiplierScale"/>
        /// the active multiplier scale (Hyper-Drive), <paramref name="shieldMultiplier"/> the owner's
        /// live Titanium Mirror factor, and <paramref name="catalyst"/> whether The Catalyst makes
        /// Y/W/H count as both vowel and consonant for trigger evaluation.
        /// </summary>
        public static WordContext Build(
            string normalizedWord,
            char? bannedLetter,
            double remainingSeconds,
            int shotClockSeconds,
            double multiplierScale,
            double shieldMultiplier = 1.0,
            bool catalyst = false)
        {
            int vowels = 0;
            int consonants = 0;
            foreach (char c in normalizedWord)
            {
                if (c is < 'a' or > 'z') continue;

                // The Catalyst: Y, W and H count as both a vowel and a consonant simultaneously.
                if (catalyst && CatalystAmbiguous.Contains(c))
                {
                    vowels++;
                    consonants++;
                }
                else if (VowelSet.Contains(c))
                {
                    vowels++;
                }
                else
                {
                    consonants++;
                }
            }

            bool containsBanned = bannedLetter is { } b && normalizedWord.Contains(b);
            bool hasDoubleLetter = HasAdjacentDuplicate(normalizedWord);

            return new WordContext(
                normalizedWord,
                normalizedWord.Length,
                vowels,
                consonants,
                bannedLetter,
                containsBanned)
            {
                RemainingSeconds = remainingSeconds,
                ShotClockSeconds = shotClockSeconds,
                MultiplierScale = multiplierScale,
                ShieldMultiplier = shieldMultiplier,
                HasDoubleLetter = hasDoubleLetter,
            };
        }

        /// <summary>True when two equal letters sit adjacent anywhere in the word (the 'ff' in
        /// <i>coffin</i>). Only ASCII letters count toward a double.</summary>
        private static bool HasAdjacentDuplicate(string word)
        {
            for (int i = 1; i < word.Length; i++)
            {
                char c = word[i];
                if (c is >= 'a' and <= 'z' && c == word[i - 1])
                    return true;
            }
            return false;
        }
    }
}
