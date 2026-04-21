using KnockBox.Spardle.Models;

namespace KnockBox.Spardle.Services;

/// <summary>
/// Read-only access to the Spardle word pools.
/// Lookups take <see cref="ReadOnlySpan{T}"/> of char so callers can avoid
/// allocating substrings on hot paths (e.g., compound word decomposition).
/// </summary>
public interface IWordListService
{
    /// <summary>
    /// True if <paramref name="word"/> exists in the full dictionary (union of NYT + Google-10k).
    /// Case-insensitive. Non-ASCII queries return false.
    /// </summary>
    bool IsValidWord(ReadOnlySpan<char> word);

    /// <summary>
    /// True if <paramref name="word"/> exists in the pool identified by <paramref name="mode"/>.
    /// Modes without a backing pool (HostDefined, CsvUpload) always return false.
    /// </summary>
    bool IsInPool(WordPoolMode mode, ReadOnlySpan<char> word);

    /// <summary>
    /// Number of words in <paramref name="mode"/> with the given <paramref name="length"/>.
    /// Returns 0 for unbacked modes or lengths that have no entries.
    /// </summary>
    int GetWordCount(WordPoolMode mode, int length);

    /// <summary>
    /// Returns the <paramref name="index"/>-th word (sorted ordinal) of length
    /// <paramref name="length"/> in <paramref name="mode"/> as raw lowercase ASCII bytes.
    /// The returned span aliases the service's internal buffer and is valid for the
    /// service's lifetime, but must not be stored across <c>await</c> (it is a ref struct).
    /// Callers that need a <see cref="string"/> should decode with
    /// <see cref="System.Text.Encoding.ASCII"/>.<c>GetString</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="mode"/> has no backing pool, no words of <paramref name="length"/>,
    /// or <paramref name="index"/> is outside <c>[0, GetWordCount(mode, length))</c>.
    /// </exception>
    ReadOnlySpan<byte> GetWord(WordPoolMode mode, int length, int index);
}
