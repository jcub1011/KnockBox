using System.Text;

namespace KnockBox.WordService.Contracts;

/// <summary>Convenience helpers for <see cref="IWordPool"/>.</summary>
public static class WordPoolExtensions
{
    /// <summary>
    /// Picks two distinct words from the pool, decoded to strings.
    /// <paramref name="next"/> must return a uniformly random integer in
    /// <c>[0, exclusiveMax)</c> for the supplied <c>exclusiveMax</c> (e.g.
    /// <c>max =&gt; rng.GetRandomInt(max)</c>) — kept as a delegate so this
    /// contracts assembly stays free of any RNG dependency.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the pool has fewer than two words.</exception>
    public static (string Start, string Destination) RandomDistinctPair(this IWordPool pool, Func<int, int> next)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(next);

        var count = pool.TotalWordCount;
        if (count < 2)
            throw new InvalidOperationException(
                $"A word pool needs at least two words to draw a distinct pair; it has {count}.");

        // Draw two distinct indices: pick j from one fewer slot, then skip past i.
        var i = next(count);
        var j = next(count - 1);
        if (j >= i) j++;

        return (
            Encoding.ASCII.GetString(pool.GetWord(i)),
            Encoding.ASCII.GetString(pool.GetWord(j)));
    }
}
