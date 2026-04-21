using System.Text;

namespace KnockBox.Spardle.Services;

/// <summary>
/// Read-only set of fixed-length ASCII words backed by a packed byte buffer.
/// Words live at <c>_buffer[i*WordLength .. (i+1)*WordLength)</c>, sorted ordinal.
/// Lookups are O(log N) and allocation-free; random access is O(1).
/// </summary>
public sealed class WordPool
{
    private const int MaxQueryLength = 64;

    public int WordLength { get; }
    private readonly byte[] _buffer;

    public int WordCount => _buffer.Length / WordLength;

    private WordPool(int wordLength, byte[] buffer)
    {
        WordLength = wordLength;
        _buffer = buffer;
    }

    public static WordPool Build(int wordLength, IEnumerable<string> words)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wordLength);

        var sorted = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in words)
        {
            if (raw is null) continue;
            var trimmed = raw.Trim();
            if (trimmed.Length != wordLength) continue;
            sorted.Add(trimmed.ToLowerInvariant());
        }

        var buffer = new byte[sorted.Count * wordLength];
        int pos = 0;
        foreach (var w in sorted)
        {
            Encoding.ASCII.GetBytes(w, 0, wordLength, buffer, pos);
            pos += wordLength;
        }
        return new WordPool(wordLength, buffer);
    }

    public bool Contains(ReadOnlySpan<char> query)
    {
        if (query.Length != WordLength || query.Length > MaxQueryLength) return false;

        Span<byte> needle = stackalloc byte[query.Length];
        for (int i = 0; i < query.Length; i++)
        {
            char c = query[i];
            if (c > 127) return false;
            needle[i] = (byte)(c is >= 'A' and <= 'Z' ? c + 32 : c);
        }

        int lo = 0, hi = WordCount - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            ReadOnlySpan<byte> entry = _buffer.AsSpan(mid * WordLength, WordLength);
            int cmp = entry.SequenceCompareTo(needle);
            if (cmp == 0) return true;
            if (cmp < 0) lo = mid + 1;
            else hi = mid - 1;
        }
        return false;
    }

    public ReadOnlySpan<byte> GetWord(int index)
    {
        if ((uint)index >= (uint)WordCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _buffer.AsSpan(index * WordLength, WordLength);
    }
}
