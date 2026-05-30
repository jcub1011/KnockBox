using KnockBox.Core.Services.Logic.RandomGeneration;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace KnockBox.LinkedList.Services.Logic
{
    /// <summary>A curated start/destination word pair (§8.4).</summary>
    public sealed record WordPair(string Start, string Destination);

    /// <summary>
    /// Loads the curated start/destination word pairs from the plugin's embedded
    /// <c>Data/start-destination-pairs.json</c> resource and supplies a random
    /// pick. Reads the resource via <see cref="Assembly.GetManifestResourceStream(string)"/>
    /// (no <c>System.IO</c> file access — KB100x compliant) once at construction.
    /// </summary>
    public sealed class WordPairSource
    {
        private const string ResourceFileName = "start-destination-pairs.json";

        private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

        /// <summary>The raw curated list, e.g. for a lobby picker.</summary>
        public ImmutableArray<WordPair> Pairs { get; }

        public WordPairSource()
        {
            Pairs = LoadPairs();
        }

        private static ImmutableArray<WordPair> LoadPairs()
        {
            var assembly = typeof(WordPairSource).Assembly;
            var resourceName = Array.Find(
                assembly.GetManifestResourceNames(),
                n => n.EndsWith(ResourceFileName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Embedded resource ending in '{ResourceFileName}' was not found in assembly '{assembly.GetName().Name}'.");

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Could not open embedded resource stream '{resourceName}'.");

            var pairs = JsonSerializer.Deserialize<List<WordPair>>(stream, s_jsonOptions);
            if (pairs is null || pairs.Count == 0)
                throw new InvalidOperationException($"Embedded resource '{resourceName}' contained no word pairs.");

            return [.. pairs];
        }

        /// <summary>Picks a random curated pair using the supplied RNG.</summary>
        public WordPair Random(IRandomNumberService rng)
        {
            ArgumentNullException.ThrowIfNull(rng);
            return Pairs[rng.GetRandomInt(Pairs.Length)];
        }
    }
}
