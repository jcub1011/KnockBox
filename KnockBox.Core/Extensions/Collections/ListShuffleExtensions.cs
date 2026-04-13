using KnockBox.Core.Services.Logic.RandomGeneration;

namespace KnockBox.Core.Extensions.Collections;

public static class ListShuffleExtensions
{
    /// <summary>
    /// Shuffles the list in-place using the Fisher-Yates algorithm.
    /// </summary>
    public static void Shuffle<T>(this IList<T> list, IRandomNumberService rng)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.GetRandomInt(0, n + 1);
            (list[n], list[k]) = (list[k], list[n]);
        }
    }
}
