using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.WordService.Contracts;
using System.Reflection;

namespace KnockBox.LinkedList.Services.Logic
{
    /// <summary>A start/destination word pair — two distinct words for one journey.</summary>
    public sealed record WordPair(string Start, string Destination);

    /// <summary>
    /// Supplies a random start/destination word pair for a Linked List journey.
    /// Linked List's audited word list ships embedded in this plugin
    /// (<c>Data/words.csv</c>); on construction it is registered once with the shared
    /// <see cref="IWordListService"/> as a custom <see cref="IWordPool"/>, reusing that
    /// library's memory-efficient length-bucketed storage instead of holding the words
    /// here. The embedded resource is read via
    /// <see cref="Assembly.GetManifestResourceStream(string)"/> (no <c>System.IO</c>
    /// file access — KB100x compliant).
    /// </summary>
    public sealed class WordSource
    {
        private const string ResourceFileName = "words.csv";

        /// <summary>The custom pool name this game registers its audited list under.</summary>
        public const string PoolName = "linked-list";

        private readonly IWordPool _pool;

        public WordSource(IWordListService wordListService)
        {
            ArgumentNullException.ThrowIfNull(wordListService);
            _pool = wordListService.RegisterCustomPool(PoolName, LoadEmbeddedWords());

            if (_pool.TotalWordCount < 2)
                throw new InvalidOperationException(
                    $"Embedded resource '{ResourceFileName}' must contain at least two distinct words.");
        }

        /// <summary>The number of distinct words available for a journey.</summary>
        public int WordCount => _pool.TotalWordCount;

        /// <summary>Picks two distinct words (start ≠ destination) using the supplied RNG.</summary>
        public WordPair RandomPair(IRandomNumberService rng)
        {
            ArgumentNullException.ThrowIfNull(rng);

            var (start, destination) = _pool.RandomDistinctPair(max => rng.GetRandomInt(max));
            // Stored lowercase; the game displays words uppercase.
            return new WordPair(start.ToUpperInvariant(), destination.ToUpperInvariant());
        }

        private static IEnumerable<string> LoadEmbeddedWords()
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

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    yield return trimmed;
            }
        }
    }
}
