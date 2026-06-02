using KnockBox.Core.Services.Logic.RandomGeneration;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data
{
    /// <summary>
    /// The legal banned-letter pools, keyed by <see cref="BanLetterMode"/>. Used by
    /// <c>SetupState</c> (initial draw), <c>IntermissionState</c> (Sniper Ban pick +
    /// timeout fallback), and the Sniper Ban picker UI (which only offers legal letters).
    /// All letters are lower-case so chain/contains checks against normalized words are direct.
    /// </summary>
    public static class BanLetterPool
    {
        private const string Vowels = "aeiou";
        private const string Consonants = "bcdfghjklmnpqrstvwxyz"; // 21 letters
        private const string AllLetters = "abcdefghijklmnopqrstuvwxyz";

        /// <summary>Returns the legal lower-case letters for <paramref name="mode"/>.</summary>
        public static string For(BanLetterMode mode) => mode switch
        {
            BanLetterMode.Vowels => Vowels,
            BanLetterMode.Consonants => Consonants,
            _ => AllLetters,
        };

        /// <summary>True when <paramref name="letter"/> (case-insensitive) is legal under <paramref name="mode"/>.</summary>
        public static bool IsLegal(BanLetterMode mode, char letter)
            => For(mode).Contains(char.ToLowerInvariant(letter));

        /// <summary>Draws a random legal letter for <paramref name="mode"/> via <paramref name="rng"/>.</summary>
        public static char Draw(BanLetterMode mode, IRandomNumberService rng)
        {
            string pool = For(mode);
            return pool[rng.GetRandomInt(pool.Length)];
        }
    }
}
