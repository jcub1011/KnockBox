namespace KnockBox.Core.Extensions.Collections
{
    public static class IListExtensions
    {
        /// <summary>
        /// Finds the index of the first matching element.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="list"></param>
        /// <param name="match"></param>
        /// <returns>The index of the element on success, -1 if no matches are found.</returns>
        public static int IndexOf<TType>(this IReadOnlyList<TType> list, Func<TType, bool> match)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (match(list[i])) return i;
            }
            return -1;
        }

        /// <summary>
        /// Removes the first matching element.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="list"></param>
        /// <param name="match"></param>
        /// <returns>If an element was removed.</returns>
        public static bool Remove<TType>(this IList<TType> list, Func<TType, bool> match)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (match(list[i]))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
    }
}
