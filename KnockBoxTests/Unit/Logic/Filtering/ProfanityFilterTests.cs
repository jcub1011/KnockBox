using System.Reflection;
using System.Text;
using KnockBox.Services.Logic.Filtering;

namespace KnockBox.Tests.Unit.Logic.Filtering;

[TestClass]
public sealed class ProfanityFilterTests
{
    private static readonly Lazy<IReadOnlyList<string>> ProfanityWords = new(LoadProfanityWords);
    private readonly ProfanityFilter _filter = new();

    [TestMethod]
    public async Task ExtractProfanitiesAsync_EmptyText_ReturnsNull()
    {
        var result = await _filter.ExtractProfanitiesAsync(string.Empty);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task ExtractProfanitiesAsync_NoMatches_ReturnsNull()
    {
        var words = ProfanityWords.Value;
        var safeChar = GetSafeChar(words);
        var text = new string(safeChar, 64);

        var result = await _filter.ExtractProfanitiesAsync(text);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task ExtractProfanitiesAsync_MatchesAllOccurrences()
    {
        var words = SelectDistinctWords(ProfanityWords.Value, 3);
        var (text, expected) = BuildTestText(words);

        var result = await _filter.ExtractProfanitiesAsync(text);

        Assert.IsNotNull(result);
        Assert.HasCount(expected.Count, result);

        for (var i = 0; i < expected.Count; i++)
        {
            Assert.AreEqual(expected[i], result[i]);
        }
    }

    private static (string Text, List<ProfanityMatch> Expected) BuildTestText(IReadOnlyList<string> words)
    {
        var safeChar = GetSafeChar(words);
        var delimiter = new string(safeChar, 3);

        var segments = new (string Value, bool Record)[]
        {
            (delimiter, false),
            (words[0], true),
            (delimiter, false),
            (words[1], true),
            (delimiter, false),
            (words[0], true),
            (delimiter, false),
            (words[2], true),
            (delimiter, false),
        };

        var expected = new List<ProfanityMatch>();
        var builder = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment.Record)
            {
                expected.Add(new ProfanityMatch(builder.Length, segment.Value.Length));
            }

            builder.Append(segment.Value);
        }

        return (builder.ToString(), expected);
    }

    private static IReadOnlyList<string> SelectDistinctWords(IReadOnlyList<string> words, int count)
    {
        var selected = new List<string>(count);

        foreach (var word in words.OrderByDescending(w => w.Length))
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            if (selected.Any(existing =>
                    existing.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                    word.Contains(existing, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selected.Add(word);

            if (selected.Count == count)
            {
                break;
            }
        }

        if (selected.Count < count)
        {
            throw new InvalidOperationException("Insufficient distinct profanities to build tests.");
        }

        return selected;
    }

    private static char GetSafeChar(IReadOnlyList<string> words)
    {
        var candidates = new[]
        {
            '§', '¶', '•', '—', '…', '¤', 'Ω', 'Ж', '☂', '☃', '✓', '✗', 'µ', 'ø', 'å', '漢', '字', '♞', '†', '‡', '☆'
        };

        foreach (var candidate in candidates)
        {
            if (words.All(word => !word.Contains(candidate)))
            {
                return candidate;
            }
        }

        return '\u0001';
    }

    private static IReadOnlyList<string> LoadProfanityWords()
    {
        var assembly = typeof(ProfanityFilter).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("English.txt", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);

        var words = new List<string>();
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            var word = line.Trim();
            if (!string.IsNullOrWhiteSpace(word))
            {
                words.Add(word);
            }
        }

        if (words.Count == 0)
        {
            throw new InvalidOperationException("Profanity list is empty.");
        }

        return words;
    }
}