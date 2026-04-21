using KnockBox.Spardle.Models;
using Microsoft.Extensions.Logging;

namespace KnockBox.Spardle.Services;

public class WordListService
{
    private static readonly string DataDir = Path.Combine(
        Path.GetDirectoryName(typeof(WordListService).Assembly.Location)!,
        "Data");

    private static readonly IReadOnlySet<string> Empty =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlySet<string> _nytStandard;
    private readonly IReadOnlySet<string> _fullDictionary;

    public WordListService(ILogger<WordListService> logger)
    {
        var ny = new HashSet<string>(
            LoadCsv(Path.Combine(DataDir, "ny-dictionary.csv"), logger),
            StringComparer.OrdinalIgnoreCase);

        var merged = new HashSet<string>(
            LoadCsv(Path.Combine(DataDir, "full-dictionary.csv"), logger),
            StringComparer.OrdinalIgnoreCase);

        merged.UnionWith(ny);

        _nytStandard = ny;
        _fullDictionary = merged;
    }

    public bool IsValidWord(string word) => _fullDictionary.Contains(word);

    public IReadOnlySet<string> GetTargetWordPool(WordPoolMode mode) => mode switch
    {
        WordPoolMode.NytStandard => _nytStandard,
        WordPoolMode.FullDictionary => _fullDictionary,
        _ => Empty,
    };

    public IReadOnlySet<string> GetFullDictionary() => _fullDictionary;

    private static IEnumerable<string> LoadCsv(string path, ILogger logger)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("WordListService: CSV file not found at [{path}].", path);
            return [];
        }

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);
    }
}
