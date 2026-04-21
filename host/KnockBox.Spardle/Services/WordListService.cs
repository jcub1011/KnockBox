using KnockBox.Spardle.Models;
using Microsoft.Extensions.Logging;

namespace KnockBox.Spardle.Services;

public sealed class WordListService : IWordListService
{
    private static readonly string DataDir = Path.Combine(
        Path.GetDirectoryName(typeof(WordListService).Assembly.Location)!,
        "Data");

    private readonly IReadOnlyDictionary<int, WordPool> _nytStandardByLength;
    private readonly IReadOnlyDictionary<int, WordPool> _fullDictionaryByLength;

    public WordListService(ILogger<WordListService> logger)
    {
        var nyWords = LoadCsv(Path.Combine(DataDir, "ny-dictionary.csv"), logger);
        var fullWords = LoadCsv(Path.Combine(DataDir, "full-dictionary.csv"), logger);

        _nytStandardByLength = BuildByLength(nyWords);
        // Full pool must include every NY word too — preserves prior UnionWith semantics.
        _fullDictionaryByLength = BuildByLength(fullWords.Concat(nyWords));
    }

    public bool IsValidWord(ReadOnlySpan<char> word)
        => _fullDictionaryByLength.TryGetValue(word.Length, out var pool) && pool.Contains(word);

    public bool IsInPool(WordPoolMode mode, ReadOnlySpan<char> word)
    {
        var byLength = GetPool(mode);
        return byLength is not null
            && byLength.TryGetValue(word.Length, out var pool)
            && pool.Contains(word);
    }

    public int GetWordCount(WordPoolMode mode, int length)
    {
        var byLength = GetPool(mode);
        if (byLength is null) return 0;
        return byLength.TryGetValue(length, out var pool) ? pool.WordCount : 0;
    }

    public ReadOnlySpan<byte> GetWord(WordPoolMode mode, int length, int index)
    {
        var byLength = GetPool(mode)
            ?? throw new ArgumentOutOfRangeException(nameof(mode), $"No backing pool for {mode}.");
        if (!byLength.TryGetValue(length, out var pool))
            throw new ArgumentOutOfRangeException(nameof(length), $"No words of length {length} in pool {mode}.");
        return pool.GetWord(index);
    }

    private IReadOnlyDictionary<int, WordPool>? GetPool(WordPoolMode mode) => mode switch
    {
        WordPoolMode.NytStandard => _nytStandardByLength,
        WordPoolMode.FullDictionary => _fullDictionaryByLength,
        _ => null,
    };

    private static IReadOnlyDictionary<int, WordPool> BuildByLength(IEnumerable<string> words)
    {
        var byLength = new Dictionary<int, List<string>>();
        foreach (var raw in words)
        {
            if (raw is null) continue;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            if (!byLength.TryGetValue(trimmed.Length, out var bucket))
            {
                bucket = new List<string>();
                byLength[trimmed.Length] = bucket;
            }
            bucket.Add(trimmed);
        }

        var result = new Dictionary<int, WordPool>(byLength.Count);
        foreach (var (length, bucket) in byLength)
        {
            result[length] = WordPool.Build(length, bucket);
        }
        return result;
    }

    private static List<string> LoadCsv(string path, ILogger logger)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("WordListService: CSV file not found at [{path}].", path);
            return new List<string>();
        }

        var result = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) result.Add(trimmed);
        }
        return result;
    }
}
