namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// An immutable snapshot of a single submission, built once per word and handed to
    /// every modifier's <c>Trigger</c>/<c>Value</c> delegate. Keeping it small and
    /// value-only means triggers can be evaluated cheaply and—critically—without
    /// capturing live game state: a card lambda never reaches back into the room.
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

        /// <summary>
        /// Builds a <see cref="WordContext"/> from a normalized word and the match's banned
        /// letter. Counts vowels/consonants over ASCII letters only; non-letter characters
        /// (which a dictionary word should not contain) are ignored by both counters.
        /// </summary>
        public static WordContext Build(string normalizedWord, char? bannedLetter)
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
                containsBanned);
        }
    }
}
