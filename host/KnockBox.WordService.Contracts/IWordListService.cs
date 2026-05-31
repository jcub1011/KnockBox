namespace KnockBox.WordService.Contracts;

/// <summary>
/// Read-only access to the shared word pools.
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
    /// An unknown/invalid mode with no backing pool always returns false.
    /// </summary>
    bool IsInPool(WordPoolMode mode, ReadOnlySpan<char> word);

    /// <summary>
    /// Number of words in <paramref name="mode"/> with the given <paramref name="length"/>.
    /// Returns 0 for an unknown/invalid mode or lengths that have no entries.
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
    /// Thrown if <paramref name="mode"/> is unknown/has no backing pool, has no words of
    /// <paramref name="length"/>, or <paramref name="index"/> is outside <c>[0, GetWordCount(mode, length))</c>.
    /// </exception>
    ReadOnlySpan<byte> GetWord(WordPoolMode mode, int length, int index);

    /// <summary>
    /// Returns the sorted distinct word lengths present in the pool.
    /// Empty for an unknown/invalid mode. Useful for
    /// populating UI controls that need to know what lengths are available.
    /// </summary>
    IEnumerable<int> GetAvailableLengths(WordPoolMode mode);

    /// <summary>
    /// Registers a caller-supplied word list as a custom, named <see cref="IWordPool"/>
    /// and returns it. The words are built into the same memory-efficient,
    /// length-bucketed storage the built-in pools use (trimmed, lowercased, deduped,
    /// sorted ordinal). Lets a consumer plugin supply its own, separately-audited list
    /// without that list being baked into this library.
    /// <para>Idempotent and thread-safe: the first call with a given
    /// <paramref name="name"/> builds and caches the pool; later calls with the same
    /// name return the cached pool and ignore <paramref name="words"/>.</para>
    /// </summary>
    /// <param name="name">Unique key identifying the custom pool.</param>
    /// <param name="words">The word list to build the pool from. Enumerated at most once.</param>
    IWordPool RegisterCustomPool(string name, IEnumerable<string> words);

    /// <summary>
    /// Returns a custom pool previously registered under <paramref name="name"/>,
    /// or <c>null</c> if no such pool exists.
    /// </summary>
    IWordPool? GetCustomPool(string name);
}
