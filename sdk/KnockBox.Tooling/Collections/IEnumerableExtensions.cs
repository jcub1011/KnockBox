using KnockBox.Core.Services.Logic.RandomGeneration;

namespace KnockBox.Tooling.Collections
{
    public static class IEnumerableExtensions
    {
        /// <summary>
        /// Returns a random item from <paramref name="items"/> where each item's probability of
        /// being selected is proportional to its weight returned by <paramref name="weightSelector"/>.
        /// </summary>
        /// <remarks>
        /// This method performs two linear scans over the enumerable: the first sums all weights
        /// and the second locates the winning item. <paramref name="weightSelector"/> is called
        /// once per item in each pass (twice total per item), so prefer lightweight selectors.
        /// No intermediate collection is allocated.
        /// </remarks>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="items">The source sequence. Must not be empty.</param>
        /// <param name="weightSelector">Returns the relative weight (must be &gt; 0) for each item.</param>
        /// <param name="rng">Random number service used for the draw.</param>
        /// <param name="randomType">Whether to use a cryptographically secure or fast RNG.</param>
        /// <returns>The randomly selected item.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <paramref name="items"/> is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when any weight is less than 1.</exception>
        public static T GetRandomWeightedItem<T>(
            this IEnumerable<T> items,
            Func<T, int> weightSelector,
            IRandomNumberService rng,
            RandomType randomType = RandomType.Fast)
        {
            // First pass: compute total weight
            int totalWeight = 0;
            foreach (var item in items)
            {
                int w = weightSelector(item);
                ArgumentOutOfRangeException.ThrowIfLessThan(w, 1, nameof(weightSelector));
                totalWeight += w;
            }

            if (totalWeight == 0)
                throw new InvalidOperationException("The source sequence must not be empty.");

            // Pick a random value in [0, totalWeight)
            int roll = rng.GetRandomInt(0, totalWeight, randomType);

            // Second pass: walk items and subtract weights until we find the winner
            foreach (var item in items)
            {
                roll -= weightSelector(item);
                if (roll < 0)
                    return item;
            }

            // Unreachable in practice, but satisfies the compiler
            throw new InvalidOperationException("Weighted selection failed unexpectedly.");
        }

        /// <summary>
        /// Returns <paramref name="source"/> as an <see cref="IReadOnlyList{T}"/>, casting it
        /// directly when it already is one (e.g. a <see cref="List{T}"/> or array) and only
        /// materializing via <see cref="Enumerable.ToList{TSource}"/> otherwise.
        /// </summary>
        /// <remarks>
        /// Use this to avoid a redundant defensive copy when a sequence flows through an API
        /// typed as <see cref="IEnumerable{T}"/> but is frequently already a list — for example
        /// when the same collection is threaded through several methods that each only enumerate
        /// it. No copy is made on the fast path; a single copy is made only for lazy sequences.
        /// <para>
        /// <b>Not a defensive copy on the fast path.</b> When <paramref name="source"/> already
        /// implements <see cref="IReadOnlyList{T}"/> the <i>same instance</i> is returned, so a
        /// later mutation of the source (e.g. adding to the backing <see cref="List{T}"/>) is
        /// observable through the returned reference. Callers that need an isolated snapshot must
        /// copy themselves (e.g. <see cref="Enumerable.ToList{TSource}"/>). This is safe when the
        /// source is already an immutable snapshot — for example <c>ConcurrentDictionary.Values</c>,
        /// which hands back a fresh <c>ReadOnlyCollection</c> on each access.
        /// </para>
        /// </remarks>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="source">The source sequence.</param>
        /// <returns>
        /// <paramref name="source"/> itself when it implements <see cref="IReadOnlyList{T}"/>;
        /// otherwise a newly materialized list of its elements.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
        public static IReadOnlyList<T> AsReadOnlyList<T>(this IEnumerable<T> source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return source as IReadOnlyList<T> ?? [.. source];
        }
    }
}
