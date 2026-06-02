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
        /// Builds a <see cref="WordContext"/> from a normalized word and the match's banned
        /// letter. Counts vowels/consonants over ASCII letters only; non-letter characters
        /// (which a dictionary word should not contain) are ignored by both counters.
        /// </summary>
        public static WordContext Build(string normalizedWord, char? bannedLetter)
            => Build(normalizedWord, bannedLetter, remainingSeconds: 0, shotClockSeconds: 0, multiplierScale: 1.0);

        /// <summary>
        /// Builds a <see cref="WordContext"/> with full turn context for time-aware and meta
        /// cards. <paramref name="remainingSeconds"/> is the submitter's clock at submit time,
        /// <paramref name="shotClockSeconds"/> the configured base, and
        /// <paramref name="multiplierScale"/> the active multiplier scale (Hyper-Drive).
        /// </summary>
        public static WordContext Build(
            string normalizedWord,
            char? bannedLetter,
            double remainingSeconds,
            int shotClockSeconds,
            double multiplierScale)
        {
            int vowels = 0;
            int consonants = 0;
            foreach (char c in normalizedWord)
            {
                if (c is < 'a' or > 'z') continue;
                if (VowelSet.Contains(c)) vowels++;
                else consonants++;
            }

            bool containsBanned = bannedLetter is { } b && normalizedWord.Contains(b);

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
            };
        }
    }
}
