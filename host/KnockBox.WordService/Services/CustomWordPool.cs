using KnockBox.WordService.Contracts;

namespace KnockBox.WordService.Services;

/// <summary>
/// <see cref="IWordPool"/> over a set of <see cref="WordPool"/>s keyed by word
/// length. Adds a contiguous global index across all lengths via a prefix-sum of
/// per-length counts, so a single random draw in <c>[0, TotalWordCount)</c> maps
/// uniformly to a word. Storage is the packed byte buffers of the underlying
/// <see cref="WordPool"/>s — roughly the size of the raw word list.
/// </summary>
internal sealed class CustomWordPool : IWordPool
{
    private readonly IReadOnlyDictionary<int, WordPool> _byLength;
    private readonly int[] _lengths;       // distinct word lengths, sorted ascending
    private readonly int[] _cumulative;    // _cumulative[k] == total words in _lengths[0..k]

    public int TotalWordCount { get; }
    public IReadOnlyList<int> AvailableLengths => _lengths;

    public CustomWordPool(IReadOnlyDictionary<int, WordPool> byLength)
    {
        _byLength = byLength;
        _lengths = byLength.Keys.OrderBy(static x => x).ToArray();
        _cumulative = new int[_lengths.Length];

        var running = 0;
        for (var k = 0; k < _lengths.Length; k++)
        {
            running += byLength[_lengths[k]].WordCount;
            _cumulative[k] = running;
        }
        TotalWordCount = running;
    }

    public int GetWordCount(int length)
        => _byLength.TryGetValue(length, out var pool) ? pool.WordCount : 0;

    public ReadOnlySpan<byte> GetWord(int length, int index)
    {
        if (!_byLength.TryGetValue(length, out var pool))
            throw new ArgumentOutOfRangeException(nameof(length), $"No words of length {length} in this pool.");
        return pool.GetWord(index);
    }

    public ReadOnlySpan<byte> GetWord(int globalIndex)
    {
        if ((uint)globalIndex >= (uint)TotalWordCount)
            throw new ArgumentOutOfRangeException(nameof(globalIndex));

        // Walk the (small) length buckets to the one whose cumulative range
        // contains globalIndex; #lengths is ~word-length span, so this is cheap.
        var k = 0;
        while (_cumulative[k] <= globalIndex) k++;
        var localIndex = globalIndex - (k == 0 ? 0 : _cumulative[k - 1]);
        return _byLength[_lengths[k]].GetWord(localIndex);
    }

    public bool Contains(ReadOnlySpan<char> word)
        => _byLength.TryGetValue(word.Length, out var pool) && pool.Contains(word);
}
