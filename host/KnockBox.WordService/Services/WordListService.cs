using KnockBox.WordService.Contracts;
using Microsoft.Extensions.Logging;

namespace KnockBox.WordService.Services;

public sealed class WordListService : IWordListService
{
    private readonly IReadOnlyDictionary<int, WordPool> _nytStandardByLength;
    private readonly IReadOnlyDictionary<int, WordPool> _fullDictionaryByLength;
    private readonly IReadOnlyDictionary<int, WordPool> _reducedByLength;

    public WordListService(ILogger<WordListService> logger)
    {
        var dataDir = ResolveDataDir();
        var nyWords = LoadCsv(Path.Combine(dataDir, "ny-dictionary.csv"), logger);
        var fullWords = LoadCsv(Path.Combine(dataDir, "full-dictionary.csv"), logger);
        var reducedWords = LoadCsv(Path.Combine(dataDir, "reduced-dictionary.csv"), logger);

        _nytStandardByLength = BuildByLength(nyWords);
        // Full pool must include every NY word too — preserves prior UnionWith semantics.
        _fullDictionaryByLength = BuildByLength(fullWords.Concat(nyWords));
        _reducedByLength = BuildByLength(reducedWords);
    }

    // Assembly.Location returns the on-disk path for plugins loaded via
    // PluginLoadContext.LoadFromAssemblyPath; falls back to the host's
    // libraries/KnockBox.WordService/Data layout for single-file publishes
    // where Location is empty.
    private static string ResolveDataDir()
    {
        var asmLocation = typeof(WordListService).Assembly.Location;
        var asmDir = string.IsNullOrEmpty(asmLocation) ? null : Path.GetDirectoryName(asmLocation);
        if (!string.IsNullOrEmpty(asmDir))
            return Path.Combine(asmDir, "Data");
        return Path.Combine(AppContext.BaseDirectory, "libraries", "KnockBox.WordService", "Data");
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

    public IEnumerable<int> GetAvailableLengths(WordPoolMode mode)
    {
        var byLength = GetPool(mode);
        if (byLength is null) return Array.Empty<int>();
        return byLength.Keys.OrderBy(x => x);
    }

    private IReadOnlyDictionary<int, WordPool>? GetPool(WordPoolMode mode) => mode switch
    {
        WordPoolMode.NytStandard => _nytStandardByLength,
        WordPoolMode.FullDictionary => _fullDictionaryByLength,
        WordPoolMode.ReducedDictionary => _reducedByLength,
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
                bucket = [];
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
        // KB1001: reading a plugin-bundled asset staged alongside the DLL
        // (Data/*.csv ships via <Content CopyToOutputDirectory>).
        // IPluginContext.Storage is for writable user data, not read-only
        // bundled content — it's the wrong destination for this.
#pragma warning disable KB1001
        if (!File.Exists(path))
        {
            logger.LogWarning("WordListService: CSV file not found at [{path}].", path);
            return [];
        }

        var result = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) result.Add(trimmed);
        }
        return result;
#pragma warning restore KB1001
    }
}
