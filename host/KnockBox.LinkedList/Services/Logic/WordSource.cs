using KnockBox.Core.Services.Logic.RandomGeneration;
using System.Collections.Immutable;
using System.Reflection;

namespace KnockBox.LinkedList.Services.Logic
{
    /// <summary>A start/destination word pair — two distinct words for one journey.</summary>
    public sealed record WordPair(string Start, string Destination);

    /// <summary>
    /// Loads the word list from the plugin's embedded <c>Data/words.csv</c> resource and
    /// supplies a random start/destination pair by picking two distinct words. Reads the
    /// resource via <see cref="Assembly.GetManifestResourceStream(string)"/> (no
    /// <c>System.IO</c> file access — KB100x compliant) once at construction.
    /// </summary>
    public sealed class WordSource
    {
        private const string ResourceFileName = "words.csv";

        private static readonly char[] s_separators = [',', '\n', '\r'];

        /// <summary>The deduplicated, uppercase word list.</summary>
        public ImmutableArray<string> Words { get; }

        public WordSource()
        {
            Words = LoadWords();
        }

        private static ImmutableArray<string> LoadWords()
        {
            var assembly = typeof(WordSource).Assembly;
            var resourceName = Array.Find(
                assembly.GetManifestResourceNames(),
                n => n.EndsWith(ResourceFileName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Embedded resource ending in '{ResourceFileName}' was not found in assembly '{assembly.GetName().Name}'.");

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Could not open embedded resource stream '{resourceName}'.");

            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();

            var words = text
                .Split(s_separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => w.ToUpperInvariant())
                .Distinct()
                .ToImmutableArray();

            if (words.Length < 2)
                throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' must contain at least two distinct words.");

            return words;
        }

        /// <summary>Picks two distinct words (start ≠ destination) using the supplied RNG.</summary>
        public WordPair RandomPair(IRandomNumberService rng)
        {
            ArgumentNullException.ThrowIfNull(rng);

            var i = rng.GetRandomInt(Words.Length);
            var j = rng.GetRandomInt(Words.Length - 1);
            if (j >= i) j++;

            return new WordPair(Words[i], Words[j]);
        }
    }
}
